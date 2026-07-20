// ============================================================
// DevPilot — IT Automation Hub v1.0
// HeartbeatService.cs — Heartbeat Monitor + AutoRestart
// Phase: 8 (PATCH: AutoRestart support added)
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Diagnostics;
using System.Net.Http;
using DevPilot.Core.Interfaces;
using DevPilot.Models;
using Microsoft.Extensions.Logging;

namespace DevPilot.Infrastructure.Services;

/// <summary>
/// Polls each HeartbeatTarget (process or URL) on a background timer.
/// Raises StatusChanged on the WPF dispatcher when status flips.
/// When AutoRestart=true and a Process target dies, relaunches it.
/// </summary>
public sealed class HeartbeatService : IHeartbeatService
{
    // ── Fields ────────────────────────────────────────────────────────────
    private readonly ILogger<HeartbeatService> _logger;
    private readonly HttpClient                _http;

    private List<HeartbeatTarget>              _targets     = new();
    private Dictionary<string, HeartbeatStatus> _lastStatus = new();
    private Dictionary<string, Process?>       _managed     = new(); // ← AutoRestart tracking
    private CancellationTokenSource?           _cts;
    private Task?                              _workerTask;
    private int                                _intervalSeconds = 30;

    // ── AutoRestart ───────────────────────────────────────────────────────
    /// <summary>
    /// When true, process targets that die are automatically relaunched.
    /// Mirrors Master Orchestrator heartbeat.auto_restart behaviour.
    /// </summary>
    public bool AutoRestart { get; set; } = false;

    // ── Events ────────────────────────────────────────────────────────────
    public event EventHandler<HeartbeatStatusChangedEventArgs>? StatusChanged;

    // ── IsRunning ─────────────────────────────────────────────────────────
    public bool IsRunning => _workerTask is { IsCompleted: false };
    // ── Constructor ───────────────────────────────────────────────────────
    public HeartbeatService(ILogger<HeartbeatService> logger)
    {
        _logger = logger;
        _http   = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    // ── IHeartbeatService ─────────────────────────────────────────────────
    public void Start(IEnumerable<HeartbeatTarget> targets, int intervalSeconds)
    {
        Stop();
        _targets         = targets.Where(t => t.IsEnabled).ToList();
        _intervalSeconds = Math.Max(5, intervalSeconds);
        _lastStatus      = new Dictionary<string, HeartbeatStatus>();
        _managed         = new Dictionary<string, Process?>();

        _cts        = new CancellationTokenSource();
        _workerTask = Task.Run(() => WorkerLoop(_cts.Token));
        _logger.LogInformation("HeartbeatService started — {Count} targets, interval={Sec}s, autoRestart={AR}",
            _targets.Count, _intervalSeconds, AutoRestart);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _workerTask?.Wait(TimeSpan.FromSeconds(3)); } catch { /* ignore */ }
        _cts?.Dispose();
        _cts        = null;
        _workerTask = null;
        _logger.LogInformation("HeartbeatService stopped.");
    }

    // ── Worker loop ───────────────────────────────────────────────────────
    private async Task WorkerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            foreach (var target in _targets)
            {
                if (ct.IsCancellationRequested) break;
                await CheckTargetAsync(target, ct).ConfigureAwait(false);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), ct)
                           .ConfigureAwait(false);
            }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task CheckTargetAsync(HeartbeatTarget target, CancellationToken ct)
    {
        HeartbeatStatus newStatus;

        if (target.Type == HeartbeatTargetType.Process)
        {
            newStatus = CheckProcess(target);

            // ── AutoRestart logic ─────────────────────────────────────────
            if (newStatus == HeartbeatStatus.Dead && AutoRestart)
            {
                TryRestart(target);
                newStatus = HeartbeatStatus.Alive; // optimistic after restart
            }
        }
        else
        {
            newStatus = await CheckUrlAsync(target.Target, ct).ConfigureAwait(false);
        }

        // Raise event only on status change
        if (!_lastStatus.TryGetValue(target.Id, out var prev) || prev != newStatus)
        {
            _lastStatus[target.Id] = newStatus;
            RaiseStatusChanged(target, newStatus);
        }
    }

    // ── Process check ─────────────────────────────────────────────────────
    private HeartbeatStatus CheckProcess(HeartbeatTarget target)
    {
        // Check if we have a managed process still alive
        if (_managed.TryGetValue(target.Id, out var managedProc) && managedProc != null)
        {
            if (!managedProc.HasExited)
                return HeartbeatStatus.Alive;
        }

        // Fallback: search by process name in running processes
        var procName = System.IO.Path.GetFileNameWithoutExtension(target.Target);
        var found    = Process.GetProcessesByName(procName).Any();
        return found ? HeartbeatStatus.Alive : HeartbeatStatus.Dead;
    }

    // ── AutoRestart ───────────────────────────────────────────────────────
    private void TryRestart(HeartbeatTarget target)
    {
        try
        {
            var psi = new ProcessStartInfo(target.Target)
            {
                UseShellExecute = true
            };
            var proc = Process.Start(psi);
            _managed[target.Id] = proc;
            _logger.LogInformation("AutoRestart: '{Name}' relaunched (PID={Pid})",
                target.Name, proc?.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AutoRestart failed for '{Name}'", target.Name);
        }
    }

    // ── URL check ─────────────────────────────────────────────────────────
    private async Task<HeartbeatStatus> CheckUrlAsync(string url, CancellationToken ct)
    {
        try
        {
            var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode
                ? HeartbeatStatus.Alive
                : HeartbeatStatus.Dead;
        }
        catch
        {
            return HeartbeatStatus.Dead;
        }
    }

    // ── Raise event on UI thread ──────────────────────────────────────────
    private void RaiseStatusChanged(HeartbeatTarget target, HeartbeatStatus status)
    {
        var prev = _lastStatus.TryGetValue(target.Id, out var p) ? p : HeartbeatStatus.Unknown;
        var args = new HeartbeatStatusChangedEventArgs(target, prev, status);
        
        if (System.Windows.Application.Current?.Dispatcher is { } d)
            d.BeginInvoke(() => StatusChanged?.Invoke(this, args));
        else
            StatusChanged?.Invoke(this, args);

        _logger.LogInformation("Heartbeat [{Status}] {Name} ({Target})",
            status, target.Name, target.Target);
    }

    // ── IDisposable ───────────────────────────────────────────────────────
    public void Dispose()
    {
        Stop();
        _http.Dispose();
    }
}
