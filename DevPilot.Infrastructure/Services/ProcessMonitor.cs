using System.Collections.Concurrent;
using System.Net.Http;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Launch;
using DevPilot.Models;
using Microsoft.Extensions.Logging;

namespace DevPilot.Infrastructure.Services;

/// <summary>
/// Tracks which tools are running (by process / command line), checks health URLs and
/// requests auto-restarts with a per-hour limit. Reports the real state — never "alive" optimistically.
/// </summary>
public sealed class ProcessMonitor : IProcessMonitor
{
    private readonly ILogger<ProcessMonitor> _logger;
    private readonly IConfigStore _config;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly ConcurrentDictionary<string, ToolStatus> _status = new();
    private readonly ConcurrentDictionary<string, HealthStatus> _health = new();
    private readonly ConcurrentDictionary<string, DateTime> _launchedAt = new();
    private readonly ConcurrentDictionary<string, string> _launchErrors = new();
    private readonly ConcurrentDictionary<string, List<DateTime>> _restarts = new();
    private readonly HashSet<string> _seenRunning = [];
    private readonly SemaphoreSlim _pollLock = new(1, 1);
    private List<ProcessInfo> _lastSnapshot = [];
    private CancellationTokenSource? _cts;
    private int _healthTick;

    public event EventHandler? StatusChanged;
    public event EventHandler<ToolEntry>? AutoRestartRequested;

    public ProcessMonitor(ILogger<ProcessMonitor> logger, IConfigStore config)
    {
        _logger = logger;
        _config = config;
    }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try { await PollAsync(token).ConfigureAwait(false); }
                catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "Monitor poll failed"); }
                try { await Task.Delay(TimeSpan.FromSeconds(_config.Current.Monitor.IntervalSeconds), token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }, token);
    }

    public ToolStatus Get(string toolId) => _status.TryGetValue(toolId, out var s) ? s : ToolStatus.Unknown(toolId);

    public HealthStatus GetHealth(string targetId) =>
        _health.TryGetValue(targetId, out var h) ? h : new HealthStatus(targetId, null, string.Empty);

    public IReadOnlyList<int> FindProcesses(ToolEntry tool)
    {
        if (!ProcessMatcher.CanTrack(tool)) return [];
        var snapshot = ProcessSnapshot.Take();
        _lastSnapshot = snapshot;
        var own = Environment.ProcessId;
        return snapshot.Where(p => p.Id != own && ProcessMatcher.Matches(tool, p)).Select(p => p.Id).ToList();
    }

    public void NotifyLaunched(string toolId, int processId)
    {
        _launchedAt[toolId] = DateTime.UtcNow;
        _launchErrors.TryRemove(toolId, out _);
        _status[toolId] = new ToolStatus(toolId, ToolRunState.Starting, processId > 0 ? [processId] : [], "Starting…");
        StatusChanged?.Invoke(this, EventArgs.Empty);
        _ = Task.Delay(1500).ContinueWith(_ => RefreshNowAsync(), TaskScheduler.Default);
    }

    public void NotifyLaunchFailed(string toolId, string error)
    {
        _launchErrors[toolId] = error;
        _status[toolId] = new ToolStatus(toolId, ToolRunState.Failed, [], error);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task RefreshNowAsync() => PollAsync(CancellationToken.None);

    private async Task PollAsync(CancellationToken ct)
    {
        if (!await _pollLock.WaitAsync(0, ct).ConfigureAwait(false)) return;
        try
        {
            var settings = _config.Current;
            var tools = settings.Tools.ToList();
            var snapshot = await Task.Run(ProcessSnapshot.Take, ct).ConfigureAwait(false);
            _lastSnapshot = snapshot;
            var own = Environment.ProcessId;
            bool changed = false;

            foreach (var tool in tools)
            {
                ToolStatus next;
                if (ProcessMatcher.CanTrack(tool))
                {
                    var pids = snapshot.Where(p => p.Id != own && ProcessMatcher.Matches(tool, p)).Select(p => p.Id).ToList();
                    if (pids.Count > 0)
                    {
                        next = new ToolStatus(tool.Id, ToolRunState.Running, pids, pids.Count == 1 ? $"PID {pids[0]}" : $"{pids.Count} processes");
                        lock (_seenRunning) _seenRunning.Add(tool.Id);
                        _launchErrors.TryRemove(tool.Id, out _);
                    }
                    else if (_launchedAt.TryGetValue(tool.Id, out var at) && DateTime.UtcNow - at < TimeSpan.FromSeconds(12))
                        next = new ToolStatus(tool.Id, ToolRunState.Starting, [], "Starting…");
                    else if (_launchErrors.TryGetValue(tool.Id, out var err))
                        next = new ToolStatus(tool.Id, ToolRunState.Failed, [], err);
                    else
                        next = new ToolStatus(tool.Id, ToolRunState.Stopped, [], "Not running");
                }
                else if (!string.IsNullOrWhiteSpace(tool.Monitor.HealthUrl))
                {
                    var h = GetHealth("tool:" + tool.Id);
                    next = new ToolStatus(tool.Id, h.Alive == true ? ToolRunState.Running : h.Alive == false ? ToolRunState.Stopped : ToolRunState.Unknown, [], h.Detail);
                }
                else continue;

                var prev = Get(tool.Id);
                if (prev.State != next.State || !prev.ProcessIds.SequenceEqual(next.ProcessIds) || prev.Detail != next.Detail)
                {
                    _status[tool.Id] = next;
                    changed = true;
                    if (prev.State == ToolRunState.Running && next.State == ToolRunState.Stopped)
                        ConsiderRestart(tool);
                }
            }

            // Health URLs every second poll (they are slower).
            if (_healthTick++ % 2 == 0)
                changed |= await CheckHealthAsync(settings, tools, ct).ConfigureAwait(false);

            if (changed) StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _pollLock.Release();
        }
    }

    private void ConsiderRestart(ToolEntry tool)
    {
        if (!tool.Monitor.AutoRestart) return;
        bool seen;
        lock (_seenRunning) seen = _seenRunning.Contains(tool.Id);
        if (!seen) return;
        var list = _restarts.GetOrAdd(tool.Id, _ => []);
        lock (list)
        {
            list.RemoveAll(t => DateTime.UtcNow - t > TimeSpan.FromHours(1));
            if (list.Count >= Math.Max(1, tool.Monitor.MaxRestartsPerHour))
            {
                _logger.LogWarning("Auto-restart limit reached for {Tool}", tool.Name);
                _launchErrors[tool.Id] = "Stopped — auto-restart limit reached";
                return;
            }
            list.Add(DateTime.UtcNow);
        }
        _logger.LogInformation("Auto-restart requested for {Tool}", tool.Name);
        AutoRestartRequested?.Invoke(this, tool);
    }

    private async Task<bool> CheckHealthAsync(AppSettings settings, List<ToolEntry> tools, CancellationToken ct)
    {
        bool changed = false;
        var checks = new List<(string Id, string Target, bool IsUrl)>();
        foreach (var t in tools.Where(t => !string.IsNullOrWhiteSpace(t.Monitor.HealthUrl)))
            checks.Add(("tool:" + t.Id, t.Monitor.HealthUrl, true));
        foreach (var x in settings.Monitor.ExtraTargets.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Target)))
            checks.Add((x.Id, x.Target, x.IsUrl));

        foreach (var (id, target, isUrl) in checks)
        {
            HealthStatus next;
            if (isUrl)
            {
                try
                {
                    using var resp = await _http.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    next = new HealthStatus(id, resp.IsSuccessStatusCode, $"HTTP {(int)resp.StatusCode}");
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
                {
                    if (ct.IsCancellationRequested) return changed;
                    next = new HealthStatus(id, false, ex is TaskCanceledException ? "Timeout" : "Unreachable");
                }
            }
            else
            {
                var name = Path.GetFileNameWithoutExtension(target);
                int count = _lastSnapshot.Count(p => string.Equals(Path.GetFileNameWithoutExtension(p.Name), name, StringComparison.OrdinalIgnoreCase));
                next = new HealthStatus(id, count > 0, count > 0 ? $"{count} running" : "Not running");
            }
            var prev = GetHealth(id);
            if (prev.Alive != next.Alive || prev.Detail != next.Detail)
            {
                _health[id] = next;
                changed = true;
            }
        }
        return changed;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _http.Dispose();
    }
}
