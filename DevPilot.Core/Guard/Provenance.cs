using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DevPilot.Models;

namespace DevPilot.Core.Guard;

/// <summary>Where a tool came from and what its files hash to right now.</summary>
public sealed record Provenance(string GitRemote, string GitCommit, string ManifestDigest, int FileCount, bool Truncated);

/// <summary>Reads git remote/commit straight from the <c>.git</c> files (no git.exe) and hashes the tool's files.</summary>
public static class ProvenanceReader
{
    public const int MaxFiles = 5000;
    private const int MaxParentSearch = 6;

    public static Provenance Read(string folder)
    {
        var (remote, commit) = ReadGit(folder);
        var (digest, count, truncated) = HashFiles(folder);
        return new Provenance(remote, commit, digest, count, truncated);
    }

    // ── git ─────────────────────────────────────────────────────────────

    internal static (string Remote, string Commit) ReadGit(string folder)
    {
        var gitDir = FindGitDir(folder);
        if (gitDir is null) return (string.Empty, string.Empty);
        return (ReadRemote(gitDir), ReadCommit(gitDir));
    }

    private static string? FindGitDir(string folder)
    {
        var dir = folder;
        for (int i = 0; i < MaxParentSearch && !string.IsNullOrEmpty(dir); i++)
        {
            var dotGit = Path.Combine(dir, ".git");
            if (Directory.Exists(dotGit)) return dotGit;
            if (File.Exists(dotGit))
            {
                try
                {
                    var first = File.ReadLines(dotGit).FirstOrDefault() ?? string.Empty;
                    if (first.StartsWith("gitdir:", StringComparison.Ordinal))
                    {
                        var target = first["gitdir:".Length..].Trim();
                        var full = Path.IsPathRooted(target) ? target : Path.GetFullPath(Path.Combine(dir, target));
                        if (Directory.Exists(full)) return full;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                return null;
            }
            dir = Path.GetDirectoryName(dir) ?? string.Empty;
        }
        return null;
    }

    private static string ReadCommit(string gitDir)
    {
        try
        {
            var headFile = Path.Combine(gitDir, "HEAD");
            if (!File.Exists(headFile)) return string.Empty;
            var head = File.ReadAllText(headFile).Trim();
            if (!head.StartsWith("ref:", StringComparison.Ordinal)) return IsHash(head) ? head : string.Empty;

            var refName = head["ref:".Length..].Trim();
            var refFile = Path.Combine(gitDir, refName.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(refFile))
            {
                var hash = File.ReadAllText(refFile).Trim();
                if (IsHash(hash)) return hash;
            }
            var packed = Path.Combine(gitDir, "packed-refs");
            if (File.Exists(packed))
            {
                foreach (var line in File.ReadLines(packed))
                {
                    if (line.Length == 0 || line[0] is '#' or '^') continue;
                    var parts = line.Split(' ', 2);
                    if (parts.Length == 2 && parts[1].Trim() == refName && IsHash(parts[0])) return parts[0];
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return string.Empty;
    }

    private static string ReadRemote(string gitDir)
    {
        try
        {
            var config = Path.Combine(gitDir, "config");
            if (!File.Exists(config)) return string.Empty;
            bool inOrigin = false;
            foreach (var raw in File.ReadLines(config))
            {
                var line = raw.Trim();
                if (line.StartsWith('['))
                {
                    inOrigin = Regex.IsMatch(line, "^\\[remote\\s+\"origin\"\\]", RegexOptions.IgnoreCase);
                    continue;
                }
                if (!inOrigin) continue;
                var m = Regex.Match(line, "^url\\s*=\\s*(.+)$", RegexOptions.IgnoreCase);
                if (m.Success) return StripCredentials(m.Groups[1].Value.Trim());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return string.Empty;
    }

    /// <summary>Removes <c>user:password@</c> from a URL so credentials never get stored.</summary>
    internal static string StripCredentials(string url) =>
        Regex.Replace(url, "(?<=://)[^/@\\s]+@", string.Empty);

    private static bool IsHash(string s) => s.Length is 40 or 64 && s.All(Uri.IsHexDigit);

    // ── files ───────────────────────────────────────────────────────────

    internal static (string Digest, int Count, bool Truncated) HashFiles(string folder)
    {
        if (!Directory.Exists(folder)) return (string.Empty, 0, false);
        var entries = new List<string>();
        bool truncated = false;
        foreach (var file in EnumerateAllFiles(folder))
        {
            if (entries.Count >= MaxFiles) { truncated = true; break; }
            string hash;
            try
            {
                using var stream = File.OpenRead(file);
                hash = Convert.ToHexString(SHA256.HashData(stream));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { hash = "unreadable"; }
            var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
            entries.Add(relative + "\0" + hash);
        }
        entries.Sort(StringComparer.Ordinal);
        var all = Encoding.UTF8.GetBytes(string.Join('\n', entries));
        return (Convert.ToHexString(SHA256.HashData(all)), entries.Count, truncated);
    }

    private static IEnumerable<string> EnumerateAllFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            string[] subdirs, files;
            try
            {
                subdirs = Directory.GetDirectories(dir);
                files = Directory.GetFiles(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (var f in files) yield return f;
            Array.Sort(subdirs, StringComparer.OrdinalIgnoreCase);
            foreach (var d in subdirs)
                if (!ToolScanner.Skipped.Contains(Path.GetFileName(d))) pending.Push(d);
        }
    }
}

public enum DriftState { NotApproved, Unchanged, Changed }

public sealed record DriftResult(DriftState State, IReadOnlyList<string> Reasons)
{
    public bool NeedsReview => State != DriftState.Unchanged;
}

/// <summary>Creates approvals and tells whether a tool still matches the one that was approved.</summary>
public static class GuardApprovals
{
    public static GuardApproval Create(Provenance current, EgressMode mode, ToolNetworkPolicy? network, DateTime nowUtc) => new()
    {
        ManifestDigest = current.ManifestDigest,
        FileCount = current.FileCount,
        GitRemote = current.GitRemote,
        GitCommit = current.GitCommit,
        ApprovedUtc = nowUtc,
        ApprovedMode = mode.ToString().ToLowerInvariant(),
        AllowedHosts = [.. network?.AllowedHosts ?? []],
        Ports = [.. network?.Ports ?? []],
        AllowNone = network?.AllowNone ?? false,
    };

    /// <summary>Any change of the file contents or of the git remote after approval means a new review.</summary>
    public static DriftResult CheckDrift(GuardApproval? approval, Provenance current)
    {
        if (approval is null) return new DriftResult(DriftState.NotApproved, []);
        var reasons = new List<string>();
        if (!string.Equals(approval.ManifestDigest, current.ManifestDigest, StringComparison.Ordinal))
            reasons.Add("Files changed since approval");
        if (!string.Equals(approval.GitRemote, current.GitRemote, StringComparison.OrdinalIgnoreCase))
            reasons.Add("Git remote changed since approval");
        return reasons.Count == 0
            ? new DriftResult(DriftState.Unchanged, [])
            : new DriftResult(DriftState.Changed, reasons);
    }
}
