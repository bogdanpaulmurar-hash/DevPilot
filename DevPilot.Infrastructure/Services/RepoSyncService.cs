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
    private readonly IGitRunner _git;
    private readonly ICommandRunner _commands;
    private readonly IToolControl _tools;
    private readonly HashSet<string> _busy = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RepoCommandResult> _lastCommand = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<RepoSyncService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, RepoPullResult> _last = new(StringComparer.OrdinalIgnoreCase);
    private readonly DateTime _startedAt = DateTime.Now;
    private Timer? _timer;
    private bool _running;

    public RepoSyncService(IConfigStore config, IGitRunner git, ICommandRunner commands, IToolControl tools, ILogger<RepoSyncService>? logger = null)
    {
        _config = config;
        _commands = commands;
        _tools = tools;
        _git = git;
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
    /// <summary>
    /// DevPilot's own repository was updated: the new version has to be installed over the running one. The handler
    /// starts the returned command in its own window and closes DevPilot (raised on a worker thread).
    /// </summary>
    public event EventHandler<RepoCommand>? SelfUpdateRequested;
    /// <summary>Tells whether DevPilot starts with Windows now, so the update keeps that setting.</summary>
    public Func<bool>? AutostartProbe { get; set; }
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
        if (!TryBegin(key, out var refusal)) return new RepoCommandResult(repo, name, RepoCommandStatus.Busy, -1, refusal, DateTime.Now);
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
        finally { End(key); }
    }

    /// <summary>The address of the <c>origin</c> remote, or null when it cannot be read.</summary>
    public async Task<string?> GetRemoteUrlAsync(string repo, CancellationToken ct = default)
    {
        var run = await Task.Run(() => _git.RunAsync(repo, ["remote", "get-url", "origin"], RepoPull.StatusTimeout, ct), ct).ConfigureAwait(false);
        var text = run.StdOut.Trim();
        return run.ExitCode == 0 && text.Length > 0 ? text.Split('\n')[0].Trim() : null;
    }

    /// <summary>Points <c>origin</c> at a new address. False when git refused or something else runs on the repository.</summary>
    public async Task<bool> SetRemoteUrlAsync(string repo, string url, CancellationToken ct = default)
    {
        var key = RepoScanner.Normalize(repo);
        if (!TryBegin(key, out _)) return false;
        try
        {
            var run = await Task.Run(() => _git.RunAsync(repo, ["remote", "set-url", "origin", url], RepoPull.StatusTimeout, ct), ct).ConfigureAwait(false);
            _logger.LogInformation("Remote of {Repo} changed to a user-less address: exit {Exit}", repo, run.ExitCode);
            return run.ExitCode == 0;
        }
        finally { End(key); }
    }

    /// <summary>Marks a repository busy unless a pull of all or another action already runs on it.</summary>
    private bool TryBegin(string key, out string refusal)
    {
        lock (_busy)
        {
            if (_running) { refusal = "A pull of all repositories is running."; return false; }
            if (!_busy.Add(key)) { refusal = "Another action is already running on this repository."; return false; }
        }
        refusal = string.Empty;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void End(string key)
    {
        lock (_busy) _busy.Remove(key);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Update: git pull; when something new came (or on <see cref="RepoUpdateMode.ReinstallOnly"/>) close the running
    /// tools of the repository, run its install step, and start the tools again. Nothing is closed when the
    /// repository is already up to date, and nothing is started again after a failed install.
    /// </summary>
    public async Task<RepoCommandResult> UpdateAsync(string repo, RepoUpdateMode mode = RepoUpdateMode.Full,
        Action<string>? step = null, CancellationToken ct = default)
    {
        var key = RepoScanner.Normalize(repo);
        string name = mode == RepoUpdateMode.Full ? "Update" : "Reinstall";
        if (!TryBegin(key, out var refusal)) return new RepoCommandResult(repo, name, RepoCommandStatus.Busy, -1, refusal, DateTime.Now);
        try
        {
            var result = await Task.Run(() => UpdateCoreAsync(repo, name, mode, step ?? (_ => { }), ct), ct).ConfigureAwait(false);
            lock (_busy) _lastCommand[key] = result;
            _logger.LogInformation("{Name} of {Repo}: {Status}", name, repo, result.Status);
            CommandFinished?.Invoke(this, result);
            return result;
        }
        finally { End(key); }
    }

    private async Task<RepoCommandResult> UpdateCoreAsync(string repo, string name, RepoUpdateMode mode, Action<string> step, CancellationToken ct)
    {
        var transcript = new List<string>();
        RepoCommandResult Done(RepoCommandStatus status, IEnumerable<string> lines, int exit = 0) =>
            new(repo, name, status, exit, string.Join('\n', lines), DateTime.Now, ShowAllLines: true) { FullOutput = string.Join("\n\n", transcript) };

        var cfg = RepoConfigs.Find(Settings, repo);
        var plan = RepoUpdatePlanner.Plan(repo, cfg, _config.Current.Tools);
        if (!plan.Available) return Done(RepoCommandStatus.Skipped, [plan.Note.Length > 0 ? plan.Note : "Nothing to update here: no install step and no tool of this repository."]);

        var lines = new List<string>();
        if (mode == RepoUpdateMode.ReinstallOnly && RepoVersion.Read(repo).Display is { Length: > 0 } current) lines.Add("Version: " + current);
        if (mode == RepoUpdateMode.Full)
        {
            step("Pulling…");
            var pull = await _pull.PullAsync(repo, ct).ConfigureAwait(false);
            if (pull.Output.Length > 0) transcript.Add(pull.Output);
            if (!pull.IsOk)
                return Done(pull.IsSkipped ? RepoCommandStatus.Skipped : RepoCommandStatus.Failed, [pull.StatusText + ": " + pull.Message], -1);
            if (pull.Status == RepoPullStatus.UpToDate)
                return Done(RepoCommandStatus.Succeeded, [UpToDateLine(pull.After)]);
            lines.Add("Pulled: " + (pull.Message.Length > 0 ? pull.Message : "updated"));
        }

        if (plan.SelfUpdate)
        {
            // The install replaces the running program: hand it over to a window that outlives DevPilot.
            var self = RepoUpdatePlanner.SelfUpdateCommand(repo, AutostartProbe?.Invoke() ?? false);
            if (self is null) return Done(RepoCommandStatus.Failed, [.. lines, "scripts\\install.cmd was not found."], -1);
            lines.Add("DevPilot closes now, installs the new version and starts again.");
            transcript.Add("$ " + self.CommandLine);
            var done = Done(RepoCommandStatus.Succeeded, lines);
            lock (_busy) _lastCommand[RepoScanner.Normalize(repo)] = done;
            SelfUpdateRequested?.Invoke(this, self);
            return done;
        }

        bool restart = cfg?.RestartTools ?? true;
        var running = restart ? plan.Tools.Where(_tools.IsRunning).ToList() : [];
        foreach (var tool in running)
        {
            step($"Closing {tool.Name}…");
            await _tools.StopAsync(tool).ConfigureAwait(false);
        }
        if (running.Count > 0) lines.Add("Closed: " + string.Join(", ", running.Select(t => t.Name)));

        if (plan.Install is not null)
        {
            step("Installing…");
            CommandRun run;
            try { run = await _commands.RunAsync(plan.Install, repo, RepoCommandRunner.Timeout, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { run = new CommandRun(-1, string.Empty, StartError: ex.Message); }
            var install = RepoCommandRunner.ToResult(repo, "Install", run, DateTime.Now);
            transcript.Add(RepoOutput.Step(plan.Install.CommandLine, run.Output, string.Empty));
            if (!install.IsOk)
            {
                lines.Add($"Install failed ({plan.InstallText}): {install.StatusText}");
                lines.Add(RepoCommandRunner.LastLines(install.OutputTail, 4));
                if (running.Count > 0) lines.Add(string.Join(", ", running.Select(t => t.Name)) + (running.Count == 1 ? " stays closed." : " stay closed."));
                return Done(RepoCommandStatus.Failed, lines, install.ExitCode);
            }
            lines.Add("Installed: " + plan.InstallText);
        }
        else lines.Add("No install step found.");

        var failed = new List<string>();
        foreach (var tool in running)
        {
            step($"Starting {tool.Name}…");
            var started = await _tools.StartAsync(tool).ConfigureAwait(false);
            if (!started.Success) failed.Add($"{tool.Name}: {started.Error}");
        }
        if (running.Count > 0) lines.Add("Started: " + string.Join(", ", running.Select(t => t.Name).Where(n => !failed.Any(f => f.StartsWith(n + ":", StringComparison.Ordinal)))));
        if (failed.Count > 0)
        {
            lines.AddRange(failed.Select(f => "Did not start " + f));
            return Done(RepoCommandStatus.Failed, lines, -1);
        }
        return Done(RepoCommandStatus.Succeeded, lines);
    }

    private static string UpToDateLine(RepoVersionInfo version) =>
        "Already up to date" + (version.Display is { Length: > 0 } v ? $" ({v})" : string.Empty) + ". Nothing was closed or installed.";

    public IReadOnlyList<string> FindRepos() => RepoScanner.Find(Settings.Root);

    public IReadOnlyList<string> SelectedRepos() => RepoPull.Selected(FindRepos(), Settings.Excluded);

    /// <summary>Pulls every ticked repository. Returns null when a run is already going.</summary>
    public async Task<IReadOnlyList<RepoPullResult>?> PullSelectedAsync(CancellationToken ct = default) =>
        await PullSelectedAsync(installDevPilot: true, ct).ConfigureAwait(false);

    /// <summary>
    /// <paramref name="installDevPilot"/>: when DevPilot's own repository got new commits, install them over the running
    /// program (DevPilot closes and starts again). The automatic schedule passes false so it never restarts DevPilot unasked.
    /// </summary>
    public async Task<IReadOnlyList<RepoPullResult>?> PullSelectedAsync(bool installDevPilot, CancellationToken ct = default)
    {
        var repos = await Task.Run(() => SelectedRepos(), ct).ConfigureAwait(false);
        var results = await RunAsync(repos, ct).ConfigureAwait(false);
        return results is not null && installDevPilot ? HandOverSelfUpdate(results) : results;
    }

    /// <summary>Pulls one repository, ticked or not. Returns null when a run is already going.</summary>
    public async Task<RepoPullResult?> PullOneAsync(string repo, CancellationToken ct = default)
    {
        var r = await RunAsync([repo], ct, recordRun: false).ConfigureAwait(false);
        if (r is null) return null;
        r = HandOverSelfUpdate(r);
        return r is { Count: > 0 } ? r[0] : null;
    }

    /// <summary>
    /// A plain pull of DevPilot's own repository that brought new commits also installs them: the new version is
    /// useless until it replaces the running one. Skipped when Update is switched off for the repository.
    /// </summary>
    private IReadOnlyList<RepoPullResult> HandOverSelfUpdate(IReadOnlyList<RepoPullResult> results)
    {
        var list = results.ToList();
        RepoCommand? install = null;
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            if (r.Status != RepoPullStatus.Updated || !RepoUpdatePlanner.IsDevPilotRepo(r.Path)) continue;
            if (RepoConfigs.Find(Settings, r.Path) is { UpdateEnabled: false }) continue;
            if (RepoUpdatePlanner.SelfUpdateCommand(r.Path, AutostartProbe?.Invoke() ?? false) is not { } cmd) continue;
            install = cmd;
            list[i] = r with { Message = r.Message + " · installing it now: DevPilot closes and starts again" };
        }
        if (install is not null) SelfUpdateRequested?.Invoke(this, install);
        return list;
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
            var results = await PullSelectedAsync(installDevPilot: false).ConfigureAwait(false);
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
