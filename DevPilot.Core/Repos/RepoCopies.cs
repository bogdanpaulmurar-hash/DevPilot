using DevPilot.Core.Interfaces;
using DevPilot.Models;

namespace DevPilot.Core.Repos;

public enum RepoCopyKind { Cloud, Local }

/// <summary>
/// One clone of a repository. <see cref="Installs"/>: the install step runs here and the tools that run from here are
/// closed and started again on Update (the local copy when there is one, otherwise the only copy).
/// </summary>
public sealed record RepoCopy(RepoCopyKind Kind, string Path, bool Installs)
{
    public string Label => Kind == RepoCopyKind.Cloud ? "cloud" : "local";
}

/// <summary>
/// The copies of a repository: the cloud copy (on the synced drive; by default the clone found under the root folder)
/// and an optional local copy the application runs from. With both, pull and Update run on both: the cloud copy is
/// only pulled, the local copy is pulled and installed.
/// </summary>
public static class RepoCopies
{
    public static readonly TimeSpan CloneTimeout = TimeSpan.FromMinutes(10);

    /// <summary>The cloud copy: the configured path, else the clone found under the root.</summary>
    public static string CloudPath(string repo, RepoConfig? cfg) =>
        string.IsNullOrWhiteSpace(cfg?.CloudPath) ? RepoScanner.Normalize(repo) : RepoScanner.Normalize(cfg!.CloudPath);

    /// <summary>The local copy, or null when none is set (or it is the same folder as the cloud copy).</summary>
    public static string? LocalPath(string repo, RepoConfig? cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg?.LocalPath)) return null;
        var local = RepoScanner.Normalize(cfg!.LocalPath);
        return RepoScanner.SamePath(local, CloudPath(repo, cfg)) ? null : local;
    }

    /// <summary>Cloud copy first, then the local copy; the last one installs.</summary>
    public static IReadOnlyList<RepoCopy> All(string repo, RepoConfig? cfg)
    {
        var cloud = CloudPath(repo, cfg);
        var local = LocalPath(repo, cfg);
        return local is null
            ? [new RepoCopy(RepoCopyKind.Cloud, cloud, true)]
            : [new RepoCopy(RepoCopyKind.Cloud, cloud, false), new RepoCopy(RepoCopyKind.Local, local, true)];
    }

    public static bool HasLocal(string repo, RepoConfig? cfg) => LocalPath(repo, cfg) is not null;

    /// <summary>Where Update installs and which tools it closes: the local copy when set, else the cloud copy.</summary>
    public static string InstallPath(string repo, RepoConfig? cfg) => All(repo, cfg)[^1].Path;

    /// <summary>
    /// The folder can receive a fresh clone: it does not exist yet, or it is an empty folder. A folder with files that
    /// is not a repository is never touched.
    /// </summary>
    public static bool CanClone(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            if (!Directory.Exists(path)) return !File.Exists(path);
            return !Directory.EnumerateFileSystemEntries(path).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>The branch a clone of <paramref name="source"/> checks out: its current branch; empty (the remote's default) on a detached HEAD.</summary>
    public static string CloneBranch(string source) =>
        RepoScanner.ReadBranch(source) is { Length: > 0 } b && b != "detached" ? b : string.Empty;

    /// <summary>A copy that is set but not cloned yet (missing or empty folder).</summary>
    public static bool NeedsClone(string path) => !RepoScanner.IsRepo(path) && CanClone(path);

    /// <summary>
    /// Clones <paramref name="source"/> (its <c>origin</c> address and current branch) into <paramref name="target"/>.
    /// Only into a missing or empty folder; nothing existing is overwritten.
    /// </summary>
    public static async Task<RepoPullResult> CloneAsync(IGitRunner git, string source, string target, CancellationToken ct = default)
    {
        static RepoPullResult R(string path, RepoPullStatus s, string m, string output = "") => new(path, s, m, DateTime.Now) { Output = output };

        if (!CanClone(target)) return R(target, RepoPullStatus.NotARepo, "The folder has files but is not a git repository; DevPilot does not clone into it.");
        if (!RepoScanner.IsRepo(source)) return R(target, RepoPullStatus.NotARepo, "There is no clone to copy the address from: " + source);

        var remote = await git.RunAsync(source, ["remote", "get-url", "origin"], RepoPull.StatusTimeout, ct).ConfigureAwait(false);
        if (remote.GitMissing) return R(target, RepoPullStatus.GitMissing, "git was not found. Install Git for Windows and restart DevPilot.");
        var url = remote.StdOut.Trim().Split('\n')[0].Trim();
        if (remote.ExitCode != 0 || url.Length == 0) return R(target, RepoPullStatus.NoUpstream, "The cloud copy has no origin remote to clone from.");

        var parent = System.IO.Path.GetDirectoryName(RepoScanner.Normalize(target));
        if (string.IsNullOrEmpty(parent)) return R(target, RepoPullStatus.Failed, "The local path has no parent folder.");
        try { Directory.CreateDirectory(parent); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return R(target, RepoPullStatus.Failed, ex.Message); }

        var args = new List<string> { "clone" };
        var branch = CloneBranch(source);
        if (branch.Length > 0) args.AddRange(["--branch", branch]);
        args.AddRange(["--", url, RepoScanner.Normalize(target)]);
        var run = await git.RunAsync(parent, args, CloneTimeout, ct).ConfigureAwait(false);
        var output = RepoOutput.Step("git " + string.Join(' ', args), run.StdOut, run.StdErr);
        if (run.GitMissing) return R(target, RepoPullStatus.GitMissing, "git was not found. Install Git for Windows and restart DevPilot.", output);
        if (run.TimedOut) return R(target, RepoPullStatus.TimedOut, "git clone did not finish in time and was stopped.", output);
        if (run.ExitCode != 0)
        {
            var status = RepoPull.Classify(run.ExitCode, run.StdOut, run.StdErr);
            if (status is RepoPullStatus.Updated or RepoPullStatus.UpToDate) status = RepoPullStatus.Failed;
            return R(target, status, RepoPull.Describe(status, run.StdOut, run.StdErr), output);
        }
        var version = RepoVersion.Read(target);
        return new RepoPullResult(target, RepoPullStatus.Updated, "Cloned" + (branch.Length > 0 ? $" ({branch})" : string.Empty), DateTime.Now)
        {
            Output = output,
            After = version,
        };
    }

    /// <summary>
    /// One result for a repository from the result of each copy: the worst status wins (failed, then skipped, then
    /// updated, then up to date), the message names each copy, the version is the one of the copy that installs.
    /// </summary>
    public static RepoPullResult Merge(string repo, IReadOnlyList<(RepoCopy Copy, RepoPullResult Result)> parts)
    {
        if (parts.Count == 0) return new RepoPullResult(repo, RepoPullStatus.Failed, "Nothing was pulled.", DateTime.Now);
        if (parts.Count == 1) return parts[0].Result with { Path = repo };

        var worst = parts.OrderByDescending(p => Severity(p.Result)).First().Result;
        var message = string.Join(" · ", parts.Select(p => $"{p.Copy.Label}: {Describe(p.Result)}"));
        var output = string.Join("\n\n", parts.Select(p =>
            $"== {p.Copy.Label} copy: {p.Copy.Path} ==\n" + (p.Result.Output.Length > 0 ? p.Result.Output : p.Result.Message)));
        var main = parts.Where(p => p.Copy.Installs).Select(p => p.Result).LastOrDefault() ?? parts[^1].Result;
        return new RepoPullResult(repo, worst.Status, message, parts.Max(p => p.Result.At))
        {
            Before = main.Before,
            After = main.After,
            Output = output,
        };
    }

    /// <summary>Higher is worse.</summary>
    public static int Severity(RepoPullResult r) => r.Status switch
    {
        RepoPullStatus.UpToDate => 0,
        RepoPullStatus.Updated => 1,
        _ when r.IsSkipped => 2,
        _ => 3,
    };

    private static string Describe(RepoPullResult r) => r.Status switch
    {
        RepoPullStatus.UpToDate => "up to date",
        RepoPullStatus.Updated => r.Message.Length > 0 ? r.Message : "updated",
        _ => r.StatusText.ToLowerInvariant() + (r.Message.Length > 0 ? " (" + r.Message + ")" : string.Empty),
    };
}
