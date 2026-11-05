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
    private readonly ICommandRunner _commands;
    private readonly HashSet<string> _busy = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RepoCommandResult> _lastCommand = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<RepoSyncService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, RepoPullResult> _last = new(StringComparer.OrdinalIgnoreCase);
    private readonly DateTime _startedAt = DateTime.Now;
    private Timer? _timer;
    private bool _running;

    public RepoSyncService(IConfigStore config, IGitRunner git, ICommandRunner commands, ILogger<RepoSyncService>? logger = null)
    {
        _config = config;
        _commands = commands;
        _pull = new RepoPull(git);
        _logger = logger ?? NullLogger<RepoSyncService>.Instance;
    }

    public RepoSettings Settings => _config.Current.Repos;
    public bool IsRunning => _running;

    /// <summary>Running state changed or a run finished.</summary>
    public event EventHandler? StateChanged;
    /// <summary>One repository finished (raised on a worker thread).</summary>
    public event EventHandler<RepoPullResult>? RepoFinished;
    /// <summary>A repository command finished (raised on a worker thread).</summary>
    public event EventHandler<RepoCommandResult>? CommandFinished;
    /// <summary>An automatic run finished; the UI decides whether to notify.</summary>
    public event EventHandler<IReadOnlyList<RepoPullResult>>? AutoRunFinished;

    /// <summary>Last result of a repository in this session, if any.</summary>
    public RepoPullResult? LastResult(string repo)
    {
        lock (_last) return _last.TryGetValue(RepoScanner.Normalize(repo), out var r) ? r : null;
    }

    /// <summary>Last command result of a repository in this session, if any.</summary>
    public RepoCommandResult? LastCommandResult(string repo)
    {
        lock (_busy) return _lastCommand.TryGetValue(RepoScanner.Normalize(repo), out var r) ? r : null;
    }

    /// <summary>True while a command runs on this repository.</summary>
    public bool IsBusy(string repo)
    {
        lock (_busy) return _busy.Contains(RepoScanner.Normalize(repo));
    }

    /// <summary>
    /// Runs one command of a repository. Returns a Busy result when something already runs on it
    /// (another command, or a pull of all repositories), so a build never collides with a pull.
    /// </summary>
    public async Task<RepoCommandResult> RunCommandAsync(string repo, RepoCommand command, CancellationToken ct = default)
    {
        var key = RepoScanner.Normalize(repo);
        var name = RepoConfigs.DisplayName(command);
        bool pulling;
        lock (_busy)
        {
            pulling = _running;
            if (pulling || !_busy.Add(key))
                return new RepoCommandResult(repo, name, RepoCommandStatus.Busy, -1,
                    pulling ? "A pull of all repositories is running." : "Another command is already running on this repository.", DateTime.Now);
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            CommandRun run;
            try { run = await Task.Run(() => _commands.RunAsync(command, repo, RepoCommandRunner.Timeout, ct), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { run = new CommandRun(-1, string.Empty, StartError: ex.Message); }
            var result = RepoCommandRunner.ToResult(repo, name, run, DateTime.Now);
            lock (_busy) _lastCommand[key] = result;
            _logger.LogInformation("Repository command {Name} in {Repo}: {Status} (exit {Exit})", name, repo, result.Status, result.ExitCode);
            CommandFinished?.Invoke(this, result);
            return result;
        }
        finally
        {
            lock (_busy) _busy.Remove(key);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public IReadOnlyList<string> FindRepos() => RepoScanner.Find(Settings.Root);

    public IReadOnlyList<string> SelectedRepos() => RepoPull.Selected(FindRepos(), Settings.Excluded);

    /// <summary>Pulls every ticked repository. Returns null when a run is already going.</summary>
    public async Task<IReadOnlyList<RepoPullResult>?> PullSelectedAsync(CancellationToken ct = default)
    {
        var repos = await Task.Run(() => SelectedRepos(), ct).ConfigureAwait(false);
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
            var free = new List<string>();
            var skipped = new List<RepoPullResult>();
            foreach (var r in repos)
            {
                if (IsBusy(r))
                {
                    var busy = new RepoPullResult(r, RepoPullStatus.Busy, "A command is running on this repository.", DateTime.Now);
                    skipped.Add(busy);
                    progress.Report(busy);
                }
                else free.Add(r);
            }
            var pulled = await Task.Run(() => _pull.PullAllAsync(free, progress, ct), ct).ConfigureAwait(false);
            var results = pulled.Concat(skipped).ToList();
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
            owner.OnRepoFinished(value);
        }
    }

    private void OnRepoFinished(RepoPullResult r) => RepoFinished?.Invoke(this, r);

    private void SetRunning(bool value)
    {
        // Same lock as the busy set, so a pull and a command can never both decide they are alone.
        lock (_busy) _running = value;
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
