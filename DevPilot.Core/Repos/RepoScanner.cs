namespace DevPilot.Core.Repos;

/// <summary>
/// Finds git clones under a root folder. Reads directory names only, a few levels deep, never descends
/// into a repository it found (submodules belong to their parent) or into dependency/build folders.
/// </summary>
public static class RepoScanner
{
    public const int MaxDepth = 3;
    public const int MaxDirectories = 3000;

    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", "packages", "__pycache__", ".venv", "venv", "env", "dist", "build",
        "TestResults", "site-packages", "$RECYCLE.BIN", "System Volume Information",
    };

    /// <summary>True when the folder has a <c>.git</c> folder, or a <c>.git</c> file (worktree / submodule).</summary>
    public static bool IsRepo(string dir) =>
        Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git"));

    /// <summary>Repositories under <paramref name="root"/> (the root itself when it is one), sorted by path.</summary>
    public static IReadOnlyList<string> Find(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return [];
        root = Path.GetFullPath(root.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (root.EndsWith(':')) root += Path.DirectorySeparatorChar;
        if (IsRepo(root)) return [root];

        var found = new List<string>();
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((root, 0));
        int visited = 0;
        while (queue.Count > 0 && visited < MaxDirectories)
        {
            var (dir, depth) = queue.Dequeue();
            IEnumerable<string> children;
            try { children = Directory.EnumerateDirectories(dir).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var child in children)
            {
                if (++visited > MaxDirectories) break;
                var name = Path.GetFileName(child);
                if (name.StartsWith('.') || Skipped.Contains(name)) continue;
                if (IsRepo(child)) { found.Add(child); continue; }
                if (depth + 1 < MaxDepth) queue.Enqueue((child, depth + 1));
            }
        }
        found.Sort(StringComparer.OrdinalIgnoreCase);
        return found;
    }

    /// <summary>Path shown in lists: relative to the root, or the folder name.</summary>
    public static string DisplayName(string root, string repo)
    {
        if (!string.IsNullOrWhiteSpace(root))
        {
            try
            {
                var rel = Path.GetRelativePath(root, repo);
                if (rel != "." && !rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel)) return rel;
            }
            catch (ArgumentException) { }
        }
        return Path.GetFileName(repo.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } n ? n : repo;
    }

    /// <summary>
    /// Current branch read from the <c>.git</c> files (no git.exe): the branch name, "detached" for a
    /// detached HEAD, or empty when it cannot be read.
    /// </summary>
    public static string ReadBranch(string repo)
    {
        try
        {
            var gitDir = Path.Combine(repo, ".git");
            if (File.Exists(gitDir))
            {
                var line = File.ReadLines(gitDir).FirstOrDefault() ?? string.Empty;
                if (!line.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase)) return string.Empty;
                var target = line["gitdir:".Length..].Trim();
                gitDir = Path.IsPathRooted(target) ? target : Path.GetFullPath(Path.Combine(repo, target));
            }
            var head = Path.Combine(gitDir, "HEAD");
            if (!File.Exists(head)) return string.Empty;
            var text = File.ReadAllText(head).Trim();
            const string prefix = "ref: refs/heads/";
            if (text.StartsWith(prefix, StringComparison.Ordinal)) return text[prefix.Length..];
            return text.Length > 0 ? "detached" : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return string.Empty; }
    }

    /// <summary>Same folder, ignoring case and a trailing separator (Windows paths).</summary>
    public static bool SamePath(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try { path = Path.GetFullPath(path.Trim()); } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.EndsWith(':') ? trimmed + Path.DirectorySeparatorChar : trimmed;
    }
}
