using DevPilot.Core.Interfaces;
using DevPilot.Core.Repos;
using DevPilot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevPilot.Infrastructure.Services;

/// <summary>
/// Runs <c>git pull --ff-only</c> on the ticked repositories under the configured root: on request
/// (hub, tray, Repositories page) and, if enabled, at start or every N minutes. One run at a time.
/// </summary>
public sealed class RepoSyncService : IDisposable
{
    private readonly IConfigStore _config;
    private readonly RepoPull _pull;
    private readonly ILogger<RepoSyncService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, RepoPullResult> _last = new(StringComparer.OrdinalIgnoreCase);
    private readonly DateTime _startedAt = DateTime.Now;
    private Timer? _timer;
    private bool _running;

    public RepoSyncService(IConfigStore config, IGitRunner git, ILogger<RepoSyncService>? logger = null)
    {
        _config = config;
        _pull = new RepoPull(git);
        _logger = logger ?? NullLogger<RepoSyncService>.Instance;
    }

    public RepoSettings Settings => _config.Current.Repos;
    public bool IsRunning => _running;

    /// <summary>Running state changed or a run finished.</summary>
    public event EventHandler? StateChanged;
    /// <summary>One repository finished (raised on a worker thread).</summary>
    public event EventHandler<RepoPullResult>? RepoFinished;
    /// <summary>An automatic run finished; the UI decides whether to notify.</summary>
    public event EventHandler<IReadOnlyList<RepoPullResult>>? AutoRunFinished;

    /// <summary>Last result of a repository in this session, if any.</summary>
    public RepoPullResult? LastResult(string repo)
    {
        lock (_last) return _last.TryGetValue(RepoScanner.Normalize(repo), out var r) ? r : null;
    }

    public IReadOnlyList<string> FindRepos() => RepoScanner.Find(Settings.Root);

    public IReadOnlyList<string> SelectedRepos() => RepoPull.Selected(FindRepos(), Settings.Excluded);

    /// <summary>Pulls every ticked repository. Returns null when a run is already going.</summary>
    public async Task<IReadOnlyList<RepoPullResult>?> PullSelectedAsync(CancellationToken ct = default)
    {
        var repos = await Task.Run(SelectedRepos, ct).ConfigureAwait(false);
        return await RunAsync(repos, ct).ConfigureAwait(false);
    }

    /// <summary>Pulls one repository, ticked or not. Returns null when a run is already going.</summary>
    public async Task<RepoPullResult?> PullOneAsync(string repo, CancellationToken ct = default)
    {
        var r = await RunAsync([repo], ct, recordRun: false).ConfigureAwait(false);
        return r is { Count: > 0 } ? r[0] : null;
    }

    private async Task<IReadOnlyList<RepoPullResult>?> RunAsync(IReadOnlyList<string> repos, CancellationToken ct, bool recordRun = true)
    {
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false)) return null;
        try
        {
            SetRunning(true);
            var progress = new Progress(this);
            var results = await Task.Run(() => _pull.PullAllAsync(repos, progress, ct), ct).ConfigureAwait(false);
            if (recordRun)
            {
                Settings.LastRun = DateTime.Now;
                _config.Touch(ConfigArea.Repos);
            }
            _logger.LogInformation("git pull on {Count} repositories: {Summary}", results.Count, RepoPull.Summary(results.ToList()));
            foreach (var r in results.Where(r => !r.IsOk))
                _logger.LogWarning("git pull {Repo}: {Status} {Message}", r.Path, r.Status, r.Message);
            return results;
        }
        finally
        {
            SetRunning(false);
            _gate.Release();
        }
    }

    private sealed class Progress(RepoSyncService owner) : IProgress<RepoPullResult>
    {
        public void Report(RepoPullResult value)
        {
            lock (owner._last) owner._last[RepoScanner.Normalize(value.Path)] = value;
            owner.RepoFinished?.Invoke(owner, value);
        }
    }

    private void SetRunning(bool value)
    {
        _running = value;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Starts the schedule check (first look after <paramref name="firstDelay"/>, then every minute).</summary>
    public void Start(TimeSpan firstDelay)
    {
        _timer?.Dispose();
        _timer = new Timer(_ => _ = TickAsync(), null, firstDelay, TimeSpan.FromMinutes(1));
    }

    private async Task TickAsync()
    {
        try
        {
            if (_running || RepoPull.NextDue(Settings, _startedAt) is not { } due || due > DateTime.Now) return;
            var results = await PullSelectedAsync().ConfigureAwait(false);
            if (results is not null) AutoRunFinished?.Invoke(this, results);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Automatic git pull failed");
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
