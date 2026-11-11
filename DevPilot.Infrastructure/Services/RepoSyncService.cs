using DevPilot.Core.Activity;
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

    public RepoSyncService(IConfigStore config, IGitRunner git, ICommandRunner commands, IToolControl tools, ILogger<RepoSyncService>? logger = null,
        ActivityTracker? activity = null, ISignInWindow? signIn = null)
    {
        _activity = activity;
        _signIn = signIn;
        _config = config;
        _commands = commands;
        _tools = tools;
        _git = git;
        _pull = new RepoPull(git);
        _logger = logger ?? NullLogger<RepoSyncService>.Instance;
    }

    private readonly ActivityTracker? _activity;
    private readonly ISignInWindow? _signIn;

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
            using var scope = _activity?.Begin($"{name} · {Path.GetFileName(repo.TrimEnd('\\', '/'))}");
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

    /// <summary>
    /// The sign-in fix (see <see cref="RepoSignIn"/>): a visible window runs <c>git fetch</c> in each existing copy and
    /// clones a local copy that is still missing, and DevPilot waits for it. Nothing in a working tree is changed.
    /// <paramref name="account"/>: sign in to the remote's site with this account for this repository; it is saved in
    /// the own git settings of each copy (<c>credential.https://host.username</c>), so it wins over an account set
    /// for the whole PC.
    /// </summary>
    public async Task<RepoSignInResult> SignInAsync(string repo, string? account = null, CancellationToken ct = default)
    {
        if (_signIn is not { } window) return new RepoSignInResult(RepoSignInOutcome.CouldNotStart, "No sign-in window is available.");
        var key = RepoScanner.Normalize(repo);
        if (!TryBegin(key, out var refusal)) return new RepoSignInResult(RepoSignInOutcome.Busy, refusal);
        try
        {
            using var scope = _activity?.Begin($"Sign-in · {Path.GetFileName(repo.TrimEnd('\\', '/'))}");
            var cfg = RepoConfigs.Find(Settings, repo);
            var cloud = RepoCopies.CloudPath(repo, cfg);
            var git = GitRunner.GitPath;
            var commands = new List<string>();
            string? url = RepoScanner.IsRepo(cloud) ? await GetRemoteUrlAsync(cloud, ct).ConfigureAwait(false) : null;
            string? accountKey = !string.IsNullOrWhiteSpace(account) && RepoRemote.Account(url) is { } site ? RepoRemote.UsernameKey(site.Site) : null;
            foreach (var copy in RepoCopies.All(repo, cfg))
            {
                if (copy.Kind == RepoCopyKind.Local && RepoCopies.NeedsClone(copy.Path))
                {
                    if (url is null) continue;
                    try
                    {
                        if (Path.GetDirectoryName(RepoScanner.Normalize(copy.Path)) is { Length: > 0 } parent) Directory.CreateDirectory(parent);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        return new RepoSignInResult(RepoSignInOutcome.CouldNotStart, ex.Message);
                    }
                    commands.Add(RepoSignIn.Clone(git, url, RepoCopies.CloneBranch(cloud), RepoScanner.Normalize(copy.Path),
                        accountKey is null ? null : $"{accountKey}={account!.Trim()}"));
                }
                else if (RepoScanner.IsRepo(copy.Path))
                {
                    if (accountKey is not null)
                    {
                        var saved = await Task.Run(() => _git.RunAsync(copy.Path, ["config", "--local", accountKey, account!.Trim()], RepoPull.StatusTimeout, ct), ct).ConfigureAwait(false);
                        _logger.LogInformation("Account for {Site} in {Copy} set to {Account}: exit {Exit}", accountKey, copy.Path, account, saved.ExitCode);
                    }
                    commands.Add(RepoSignIn.Fetch(git, copy.Path));
                }
            }
            if (commands.Count == 0)
                return new RepoSignInResult(RepoSignInOutcome.CouldNotStart, "No copy of this repository is a git clone with a remote.");

            _logger.LogInformation("Sign-in window for {Repo}: {Commands}", repo, RepoSignIn.Redact(string.Join(" ; ", commands)));
            WindowRun run;
            try { run = await Task.Run(() => window.RunAsync(cloud, RepoSignIn.Script(commands), RepoSignIn.Timeout, ct), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { run = new WindowRun(-1, Error: ex.Message); }
            var result = run.Error is not null ? new RepoSignInResult(RepoSignInOutcome.CouldNotStart, run.Error)
                       : run.TimedOut ? new RepoSignInResult(RepoSignInOutcome.TimedOut, "The sign-in window was still open after 15 minutes; DevPilot stopped waiting for it.")
                       : run.ExitCode == 0 ? new RepoSignInResult(RepoSignInOutcome.SignedIn)
                       : new RepoSignInResult(RepoSignInOutcome.Failed, "A step in the sign-in window failed (the window showed why).");
            _logger.LogInformation("Sign-in window for {Repo}: {Outcome} (exit {Exit}) {Detail}", repo, result.Outcome, run.ExitCode, result.Detail);
            return result;
        }
        finally { End(key); }
    }

    /// <summary>The <c>git status --porcelain</c> lines of the tracked files, or empty when they cannot be read.</summary>
    public async Task<string> GetChangesAsync(string repo, CancellationToken ct = default)
    {
        var text = new List<string>();
        foreach (var copy in RepoCopies.All(repo, RepoConfigs.Find(Settings, repo)))
            if (await ChangesOfAsync(copy.Path, ct).ConfigureAwait(false) is { Length: > 0 } changes) text.Add(changes.TrimEnd());
        return string.Join("\n", text);
    }

    private async Task<string> ChangesOfAsync(string path, CancellationToken ct)
    {
        if (!RepoScanner.IsRepo(path)) return string.Empty;
        var run = await Task.Run(() => _git.RunAsync(path, ["status", "--porcelain", "--untracked-files=no"], RepoPull.StatusTimeout, ct), ct).ConfigureAwait(false);
        return run.ExitCode == 0 ? run.StdOut : string.Empty;
    }

    /// <summary>
    /// The fix for local changes, after the user chose it: set the changes aside, pull, optionally put them back
    /// (see <see cref="RepoPull.StashPullAsync"/>). Null when something else runs on the repository.
    /// </summary>
    public async Task<RepoPullResult?> StashPullAsync(string repo, bool restore, CancellationToken ct = default)
    {
        var key = RepoScanner.Normalize(repo);
        if (!TryBegin(key, out _)) return null;
        try
        {
            var r = await Task.Run(() => StashPullCopiesAsync(repo, restore, ct), ct).ConfigureAwait(false);
            r = HandOverSelfUpdate([r])[0];
            lock (_last) _last[key] = r;
            _logger.LogInformation("Stash and pull of {Repo} (restore {Restore}): {Status}", repo, restore, r.Status);
            OnRepoFinished(r);
            return r;
        }
        finally { End(key); }
    }

    /// <summary>The fix on every copy: the copies with local changes are stashed and pulled, the others only pulled.</summary>
    private async Task<RepoPullResult> StashPullCopiesAsync(string repo, bool restore, CancellationToken ct)
    {
        var cfg = RepoConfigs.Find(Settings, repo);
        var parts = new List<(RepoCopy, RepoPullResult)>();
        foreach (var copy in RepoCopies.All(repo, cfg))
        {
            bool dirty = (await ChangesOfAsync(copy.Path, ct).ConfigureAwait(false)).Length > 0;
            var r = dirty ? await _pull.StashPullAsync(copy.Path, restore, ct).ConfigureAwait(false)
                          : await PullCopyAsync(repo, cfg, copy, ct).ConfigureAwait(false);
            parts.Add((copy, r));
        }
        return RepoCopies.Merge(repo, parts);
    }

    /// <summary>
    /// Pulls the copies of a repository one after another (<paramref name="only"/>: just one of them). A local copy
    /// that is not cloned yet (missing or empty folder) is cloned from the cloud copy's remote.
    /// </summary>
    private async Task<RepoPullResult> PullCopiesAsync(string repo, RepoCopyKind? only, CancellationToken ct)
    {
        var cfg = RepoConfigs.Find(Settings, repo);
        var all = RepoCopies.All(repo, cfg);
        var copies = all.Where(c => only is null || c.Kind == only).ToList();
        if (copies.Count == 0)
            return new RepoPullResult(repo, RepoPullStatus.NotARepo, "No local copy is set for this repository: add its path in the gear.", DateTime.Now);
        var parts = new List<(RepoCopy, RepoPullResult)>();
        foreach (var copy in copies)
        {
            RepoPullResult r;
            try { r = await PullCopyAsync(repo, cfg, copy, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { r = new RepoPullResult(copy.Path, RepoPullStatus.Failed, ex.Message, DateTime.Now); }
            parts.Add((copy, r));
            if (r.Status == RepoPullStatus.GitMissing) break;
        }
        var merged = RepoCopies.Merge(repo, parts);
        // One copy of a repository that has two: say which one.
        return parts.Count == 1 && all.Count > 1 ? merged with { Message = $"{parts[0].Item1.Label}: {merged.Message}" } : merged;
    }

    /// <summary>git pull on one copy; a local copy that does not exist yet is cloned instead.</summary>
    private Task<RepoPullResult> PullCopyAsync(string repo, RepoConfig? cfg, RepoCopy copy, CancellationToken ct) =>
        copy.Kind == RepoCopyKind.Local && RepoCopies.NeedsClone(copy.Path)
            ? RepoCopies.CloneAsync(_git, RepoCopies.CloudPath(repo, cfg), copy.Path, ct)
            : _pull.PullAsync(copy.Path, ct);

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
            using var scope = _activity?.Begin($"{name} · {Path.GetFileName(repo.TrimEnd('\\', '/'))}");
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
        var copies = RepoCopies.All(repo, cfg);
        var target = copies[^1];
        bool clone = target.Kind == RepoCopyKind.Local && RepoCopies.NeedsClone(target.Path);
        var plan = RepoUpdatePlanner.Plan(target.Path, cfg, _config.Current.Tools);
        if (!plan.Available && !clone) return Done(RepoCommandStatus.Skipped, [plan.Note.Length > 0 ? plan.Note : "Nothing to update here: no install step and no tool of this repository."]);
        if (clone && mode == RepoUpdateMode.ReinstallOnly)
            return Done(RepoCommandStatus.Skipped, ["The local copy is not cloned yet: run Update or Pull first. " + target.Path]);

        var lines = new List<string>();
        // Problems of the copies that only get pulled: reported, they do not stop the install of the local copy.
        var problems = new List<RepoPullResult>();
        RepoCommandResult Finish(RepoCommandStatus status, List<string> text, int exit = 0)
        {
            if (status == RepoCommandStatus.Succeeded && problems.Count > 0)
                status = problems.Any(p => !p.IsSkipped) ? RepoCommandStatus.Failed : RepoCommandStatus.Skipped;
            return Done(status, text, exit);
        }

        if (mode == RepoUpdateMode.ReinstallOnly && RepoVersion.Read(target.Path).Display is { Length: > 0 } current) lines.Add("Version: " + current);
        if (mode == RepoUpdateMode.Full)
        {
            foreach (var copy in copies.Where(c => !c.Installs))
            {
                step($"Pulling the {copy.Label} copy…");
                var other = await PullCopyAsync(repo, cfg, copy, ct).ConfigureAwait(false);
                if (other.Output.Length > 0) transcript.Add($"== {copy.Label} copy: {copy.Path} ==\n" + other.Output);
                if (other.IsOk) lines.Add($"Pulled the {copy.Label} copy: " + (other.Status == RepoPullStatus.UpToDate ? "up to date" : other.Message));
                else
                {
                    lines.Add($"The {copy.Label} copy was not pulled: {other.StatusText}: {other.Message}");
                    problems.Add(other);
                }
            }

            string where = copies.Count > 1 ? $" the {target.Label} copy" : string.Empty;
            step(clone ? "Cloning the local copy…" : copies.Count > 1 ? $"Pulling the {target.Label} copy…" : "Pulling…");
            var pull = await PullCopyAsync(repo, cfg, target, ct).ConfigureAwait(false);
            if (pull.Output.Length > 0) transcript.Add(copies.Count > 1 ? $"== {target.Label} copy: {target.Path} ==\n" + pull.Output : pull.Output);
            if (!pull.IsOk)
            {
                problems.Clear();
                return Done(pull.IsSkipped ? RepoCommandStatus.Skipped : RepoCommandStatus.Failed, [.. lines, $"{(where.Length > 0 ? "Local copy: " : string.Empty)}{pull.StatusText}: {pull.Message}"], -1);
            }
            if (pull.Status == RepoPullStatus.UpToDate)
                return Finish(RepoCommandStatus.Succeeded, [.. lines, UpToDateLine(pull.After)]);
            lines.Add((clone ? "Cloned" : "Pulled") + where + ": " + (pull.Message.Length > 0 ? pull.Message : "updated"));
            if (clone)
            {
                plan = RepoUpdatePlanner.Plan(target.Path, cfg, _config.Current.Tools);
                if (!plan.Available) return Finish(RepoCommandStatus.Succeeded, [.. lines, "No install step found and no tool runs from it."]);
            }
        }

        if (plan.SelfUpdate)
        {
            // The install replaces the running program: hand it over to a window that outlives DevPilot.
            var self = RepoUpdatePlanner.SelfUpdateCommand(target.Path, AutostartProbe?.Invoke() ?? false);
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
            try { run = await _commands.RunAsync(plan.Install, target.Path, RepoCommandRunner.Timeout, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { run = new CommandRun(-1, string.Empty, StartError: ex.Message); }
            var install = RepoCommandRunner.ToResult(target.Path, "Install", run, DateTime.Now);
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
            return Finish(RepoCommandStatus.Failed, lines, -1);
        }
        return Finish(RepoCommandStatus.Succeeded, lines);
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

    /// <summary>
    /// Pulls one repository, ticked or not: every copy, or only the cloud or the local copy. Returns null when a run is
    /// already going.
    /// </summary>
    public async Task<RepoPullResult?> PullOneAsync(string repo, CancellationToken ct = default) =>
        await PullOneAsync(repo, null, ct).ConfigureAwait(false);

    public async Task<RepoPullResult?> PullOneAsync(string repo, RepoCopyKind? only, CancellationToken ct = default)
    {
        var r = await RunAsync([repo], ct, recordRun: false, only).ConfigureAwait(false);
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
            var cfg = RepoConfigs.Find(Settings, r.Path);
            var path = RepoCopies.InstallPath(r.Path, cfg);
            if (r.Status != RepoPullStatus.Updated || !RepoUpdatePlanner.IsDevPilotRepo(path)) continue;
            if (cfg is { UpdateEnabled: false }) continue;
            if (RepoUpdatePlanner.SelfUpdateCommand(path, AutostartProbe?.Invoke() ?? false) is not { } cmd) continue;
            install = cmd;
            list[i] = r with { Message = r.Message + " · installing it now: DevPilot closes and starts again" };
        }
        if (install is not null) SelfUpdateRequested?.Invoke(this, install);
        return list;
    }

    private async Task<IReadOnlyList<RepoPullResult>?> RunAsync(IReadOnlyList<string> repos, CancellationToken ct, bool recordRun = true, RepoCopyKind? only = null)
    {
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false)) return null;
        try
        {
            SetRunning(true);
            using var scope = _activity?.Begin("Pulling repositories");
            var progress = new Progress(this, scope, repos.Count);
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
            var pulled = await Task.Run(async () =>
            {
                var list = new List<RepoPullResult>();
                foreach (var repo in free)
                {
                    ct.ThrowIfCancellationRequested();
                    var r = await PullCopiesAsync(repo, only, ct).ConfigureAwait(false);
                    list.Add(r);
                    progress.Report(r);
                    if (r.Status == RepoPullStatus.GitMissing) break;
                }
                return list;
            }, ct).ConfigureAwait(false);
            var results = pulled.Concat(skipped).ToList();
            if (recordRun)
            {
                Settings.LastRun = DateTime.Now;
                _config.Touch(ConfigArea.Repos);
            }
            _logger.LogInformation("git pull on {Count} repositories: {Summary}", results.Count, RepoPull.Summary(results.ToList()));
            // What git printed goes to the log too (addresses without user names), so a failure can be diagnosed from the file.
            foreach (var r in results.Where(r => !r.IsOk))
                _logger.LogWarning("git pull {Repo}: {Status} {Message}\n{Output}", r.Path, r.Status, r.Message,
                    RepoSignIn.Redact(RepoCommandRunner.LastLines(r.Output, 12)));
            return results;
        }
        finally
        {
            SetRunning(false);
            _gate.Release();
        }
    }

    private sealed class Progress(RepoSyncService owner, IActivityScope? scope = null, int total = 0) : IProgress<RepoPullResult>
    {
        private int _done;

        public void Report(RepoPullResult value)
        {
            if (scope is not null && total > 0)
            {
                var done = Interlocked.Increment(ref _done);
                scope.Report((double)done / total, $"{done} of {total}");
            }
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
