using DevPilot.Core.Interfaces;

namespace DevPilot.Core.Repos;

/// <summary>What happened to one repository.</summary>
public enum RepoPullStatus
{
    Updated,
    UpToDate,
    /// <summary>Tracked files have uncommitted changes: skipped, nothing touched.</summary>
    LocalChanges,
    /// <summary>The branch has no upstream (or HEAD is detached): nothing to pull from.</summary>
    NoUpstream,
    /// <summary>Local commits and new remote commits: a fast-forward is impossible, nothing touched.</summary>
    Diverged,
    /// <summary>Git refuses the folder because another account owns it (common on synced drives).</summary>
    UnsafeOwnership,
    AuthFailed,
    Offline,
    NotARepo,
    GitMissing,
    TimedOut,
    Failed,
    /// <summary>A command is running on this repository: skipped so git and the command do not collide.</summary>
    Busy,
}

public sealed record RepoPullResult(string Path, RepoPullStatus Status, string Message, DateTime At)
{
    /// <summary>Nothing for the user to do.</summary>
    public bool IsOk => Status is RepoPullStatus.Updated or RepoPullStatus.UpToDate;

    /// <summary>Skipped on purpose; the repository is fine but was left alone.</summary>
    public bool IsSkipped => Status is RepoPullStatus.LocalChanges or RepoPullStatus.NoUpstream or RepoPullStatus.Diverged or RepoPullStatus.Busy;

    public string StatusText => RepoPull.Label(Status);

    /// <summary>Version of the clone before the pull (empty when the pull did not get that far).</summary>
    public RepoVersionInfo Before { get; init; } = RepoVersionInfo.None;

    /// <summary>Version of the clone after the pull (same as <see cref="Before"/> when nothing came).</summary>
    public RepoVersionInfo After { get; init; } = RepoVersionInfo.None;

    /// <summary>"2.14.0 → 2.15.0" or "2.15.0 (unchanged)"; empty when no version is known.</summary>
    public string VersionChange => IsOk ? RepoVersion.Change(Before, After) : string.Empty;
}

/// <summary>
/// Pulls repositories safely: a repository with uncommitted changes to tracked files is skipped, and the
/// pull is fast-forward only, so DevPilot never merges, rebases or stashes anything.
/// </summary>
public sealed class RepoPull(IGitRunner git)
{
    public static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan PullTimeout = TimeSpan.FromMinutes(3);

    public async Task<RepoPullResult> PullAsync(string repo, CancellationToken ct = default)
    {
        if (!Directory.Exists(repo) || !RepoScanner.IsRepo(repo))
            return Result(repo, RepoPullStatus.NotARepo, "The folder is not a git repository (any more).");

        var status = await git.RunAsync(repo, ["status", "--porcelain", "--untracked-files=no"], StatusTimeout, ct).ConfigureAwait(false);
        if (Problem(repo, status) is { } early) return early;
        if (status.ExitCode != 0) return Result(repo, Classify(status.ExitCode, status.StdOut, status.StdErr), LastLine(status.StdErr, status.StdOut));
        int changed = CountChanges(status.StdOut);
        if (changed > 0)
            return Result(repo, RepoPullStatus.LocalChanges, $"{changed} changed file(s) not committed. Skipped; commit or stash them first.");

        var before = RepoVersion.Read(repo);
        var pull = await git.RunAsync(repo, ["pull", "--ff-only"], PullTimeout, ct).ConfigureAwait(false);
        if (Problem(repo, pull) is { } failed) return failed with { Before = before, After = before };
        var outcome = Classify(pull.ExitCode, pull.StdOut, pull.StdErr);
        var after = outcome == RepoPullStatus.Updated ? RepoVersion.Read(repo) : before;
        var message = Describe(outcome, pull.StdOut, pull.StdErr);
        if (outcome == RepoPullStatus.Updated && RepoVersion.Change(before, after) is { Length: > 0 } change)
            message = change + " · " + message;
        return Result(repo, outcome, message) with { Before = before, After = after };
    }

    /// <summary>Pulls one repository after another (gentle on synced drives), reporting each as it finishes.</summary>
    public async Task<IReadOnlyList<RepoPullResult>> PullAllAsync(IEnumerable<string> repos, IProgress<RepoPullResult>? progress = null, CancellationToken ct = default)
    {
        var results = new List<RepoPullResult>();
        foreach (var repo in repos)
        {
            ct.ThrowIfCancellationRequested();
            RepoPullResult r;
            try { r = await PullAsync(repo, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { r = Result(repo, RepoPullStatus.Failed, ex.Message); }
            results.Add(r);
            progress?.Report(r);
            if (r.Status == RepoPullStatus.GitMissing) break;
        }
        return results;
    }

    private static RepoPullResult? Problem(string repo, GitRun run)
    {
        if (run.GitMissing) return Result(repo, RepoPullStatus.GitMissing, "git was not found. Install Git for Windows and restart DevPilot.");
        if (run.TimedOut) return Result(repo, RepoPullStatus.TimedOut, "git did not finish in time and was stopped.");
        return null;
    }

    private static RepoPullResult Result(string repo, RepoPullStatus status, string message) => new(repo, status, message, DateTime.Now);

    /// <summary>Number of tracked files with changes in <c>git status --porcelain</c> output.</summary>
    public static int CountChanges(string porcelain) =>
        porcelain.Split('\n').Count(l => l.TrimEnd('\r').Length > 0 && !l.StartsWith("??", StringComparison.Ordinal) && !l.StartsWith("!!", StringComparison.Ordinal));

    /// <summary>Maps the exit code and the (English, LC_ALL=C) output of <c>git pull --ff-only</c> to a status.</summary>
    public static RepoPullStatus Classify(int exitCode, string stdout, string stderr)
    {
        var all = stdout + "\n" + stderr;
        bool Has(string s) => all.Contains(s, StringComparison.OrdinalIgnoreCase);

        if (exitCode == 0)
            return Has("Already up to date") || Has("Already up-to-date") ? RepoPullStatus.UpToDate : RepoPullStatus.Updated;
        if (Has("dubious ownership") || Has("safe.directory")) return RepoPullStatus.UnsafeOwnership;
        if (Has("not a git repository")) return RepoPullStatus.NotARepo;
        if (Has("no tracking information") || Has("not currently on a branch") || Has("no such ref was fetched")) return RepoPullStatus.NoUpstream;
        if (Has("would be overwritten") || Has("commit your changes or stash them") || Has("unstaged changes")) return RepoPullStatus.LocalChanges;
        if (Has("Not possible to fast-forward") || Has("diverging branches") || Has("have diverged") || Has("non-fast-forward")) return RepoPullStatus.Diverged;
        if (Has("Authentication failed") || Has("could not read Username") || Has("could not read Password") || Has("terminal prompts disabled")
            || Has("Permission denied (publickey") || Has("Repository not found") || Has("returned error: 403") || Has("returned error: 401"))
            return RepoPullStatus.AuthFailed;
        if (Has("Could not resolve host") || Has("Failed to connect") || Has("Connection timed out") || Has("Connection refused")
            || Has("unable to access") || Has("Could not read from remote repository") || Has("network is unreachable"))
            return RepoPullStatus.Offline;
        return RepoPullStatus.Failed;
    }

    /// <summary>One short line for the list and the notification.</summary>
    public static string Describe(RepoPullStatus status, string stdout, string stderr) => status switch
    {
        RepoPullStatus.UpToDate => "Already up to date.",
        RepoPullStatus.Updated => ChangeSummary(stdout) is { Length: > 0 } s ? s : "Updated.",
        RepoPullStatus.LocalChanges => "Local changes would be overwritten. Skipped; commit or stash them first.",
        RepoPullStatus.NoUpstream => "The current branch does not track a remote branch. Nothing to pull.",
        RepoPullStatus.Diverged => "Local and remote commits differ, so a fast-forward is not possible. Nothing changed; pull or rebase by hand.",
        RepoPullStatus.UnsafeOwnership => "git does not trust this folder (other owner). Run once: git config --global --add safe.directory \"<folder>\"",
        RepoPullStatus.AuthFailed => "Sign-in to the remote failed. Run git pull once in a terminal to sign in.",
        RepoPullStatus.Offline => "The remote cannot be reached (offline, VPN or proxy).",
        RepoPullStatus.NotARepo => "The folder is not a git repository.",
        _ => LastLine(stderr, stdout),
    };

    /// <summary>"3 files changed, 10 insertions(+), 2 deletions(-)" from the pull output, if present.</summary>
    public static string ChangeSummary(string stdout)
    {
        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Contains("changed", StringComparison.Ordinal) && (line.Contains("file", StringComparison.Ordinal))) return line;
        }
        return string.Empty;
    }

    private static string LastLine(string preferred, string fallback)
    {
        foreach (var text in new[] { preferred, fallback })
        {
            var line = text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0 && !l.StartsWith("hint:", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(line)) return line.Length > 300 ? line[..300] + "…" : line;
        }
        return "git failed without a message.";
    }

    public static string Label(RepoPullStatus status) => status switch
    {
        RepoPullStatus.Updated => "Updated",
        RepoPullStatus.UpToDate => "Up to date",
        RepoPullStatus.LocalChanges => "Skipped: local changes",
        RepoPullStatus.NoUpstream => "Skipped: no upstream",
        RepoPullStatus.Diverged => "Skipped: diverged",
        RepoPullStatus.UnsafeOwnership => "Not trusted by git",
        RepoPullStatus.AuthFailed => "Sign-in failed",
        RepoPullStatus.Offline => "Remote unreachable",
        RepoPullStatus.NotARepo => "Not a repository",
        RepoPullStatus.Busy => "Skipped: command running",
        RepoPullStatus.GitMissing => "git not found",
        RepoPullStatus.TimedOut => "Timed out",
        _ => "Failed",
    };

    /// <summary>"2 updated · 5 up to date · 1 skipped · 1 failed".</summary>
    public static string Summary(IReadOnlyCollection<RepoPullResult> results)
    {
        if (results.Count == 0) return "No repositories selected.";
        int updated = results.Count(r => r.Status == RepoPullStatus.Updated);
        int same = results.Count(r => r.Status == RepoPullStatus.UpToDate);
        int skipped = results.Count(r => r.IsSkipped);
        int failed = results.Count - updated - same - skipped;
        var parts = new List<string>();
        if (updated > 0) parts.Add($"{updated} updated");
        if (same > 0) parts.Add($"{same} up to date");
        if (skipped > 0) parts.Add($"{skipped} skipped");
        if (failed > 0) parts.Add($"{failed} failed");
        return string.Join(" · ", parts);
    }

    /// <summary>True when an automatic run is worth a notification.</summary>
    public static bool IsNoteworthy(IEnumerable<RepoPullResult> results) => results.Any(r => r.Status != RepoPullStatus.UpToDate);

    /// <summary>Repositories under the root that are ticked (not excluded).</summary>
    public static IReadOnlyList<string> Selected(IEnumerable<string> found, IEnumerable<string> excluded)
    {
        var ex = excluded.Select(RepoScanner.Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return found.Where(r => !ex.Contains(RepoScanner.Normalize(r))).ToList();
    }

    /// <summary>When the next automatic pull is due (null = never).</summary>
    public static DateTime? NextDue(DevPilot.Models.RepoSettings s, DateTime startedAt)
    {
        if (string.IsNullOrWhiteSpace(s.Root)) return null;
        return s.Auto switch
        {
            DevPilot.Models.RepoAutoPull.Off => (DateTime?)null,
            DevPilot.Models.RepoAutoPull.AtStartup => s.LastRun is { } last && last >= startedAt ? (DateTime?)null : startedAt,
            _ => s.LastRun is { } l && l >= startedAt ? l.AddMinutes(Math.Clamp(s.IntervalMinutes, DevPilot.Models.RepoSettings.MinInterval, DevPilot.Models.RepoSettings.MaxInterval)) : startedAt,
        };
    }
}
