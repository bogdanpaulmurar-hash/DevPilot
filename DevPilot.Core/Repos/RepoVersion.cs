using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevPilot.Core.Repos;

/// <summary>
/// Version of a clone as its files say (not the version of a running program) plus the short commit.
/// <see cref="Version"/> is empty when no version was found; <see cref="Commit"/> is empty when HEAD cannot be read.
/// </summary>
public sealed record RepoVersionInfo(string Version, string Commit, string Source = "")
{
    public static readonly RepoVersionInfo None = new(string.Empty, string.Empty);

    public bool IsEmpty => Version.Length == 0 && Commit.Length == 0;

    /// <summary>"2.15.0", or the short commit when the files hold no version, or empty.</summary>
    public string Display => Version.Length > 0 ? Version : Commit;

    /// <summary>Tooltip text: where the version came from and the commit.</summary>
    public string Tip =>
        IsEmpty ? string.Empty
        : Version.Length > 0 ? $"Version {Version} from {Source}" + (Commit.Length > 0 ? $" · commit {Commit}" : string.Empty)
        : $"No version found in the files · commit {Commit}";
}

/// <summary>
/// Reads the version of a clone from its files, without git.exe and without running anything. Order:
/// <c>devpilot.tool.json</c> "version" → <c>Directory.Build.props</c> / csproj <c>&lt;Version&gt;</c> →
/// <c>pyproject.toml</c> → Python <c>__version__</c> / <c>VERSION</c> in <c>version.py</c> or a package
/// <c>__init__.py</c> → <c>package.json</c> → a <c>VERSION</c> / <c>version.txt</c> file. Only a few small files
/// near the top of the clone are opened.
/// </summary>
public static class RepoVersion
{
    private const long MaxFileBytes = 256 * 1024;
    private const int ShortCommit = 7;

    private static readonly HashSet<string> SkippedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", "packages", "__pycache__", ".venv", "venv", "env", "dist", "build",
        "TestResults", "site-packages", "tests", "test", "docs", "scripts",
    };

    private static readonly Regex XmlVersion = new(@"<Version>\s*([^<$\s][^<]*?)\s*</Version>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex XmlVersionPrefix = new(@"<VersionPrefix>\s*([^<$\s][^<]*?)\s*</VersionPrefix>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AppProject = new(@"<OutputType>\s*(WinExe|Exe)\s*</OutputType>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex TomlVersion = new(@"^\s*version\s*=\s*[""']([^""']+)[""']", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex PyVersion = new(@"^(?:__version__|VERSION|APP_VERSION)\s*(?::\s*\w+\s*)?=\s*[""']([^""']+)[""']", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>Version and short commit of the clone; never throws.</summary>
    public static RepoVersionInfo Read(string repo)
    {
        if (string.IsNullOrWhiteSpace(repo) || !Directory.Exists(repo)) return RepoVersionInfo.None;
        var commit = ReadCommit(repo);
        try
        {
            if (Find(repo) is { } found) return new RepoVersionInfo(found.Version, commit, found.Source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        return new RepoVersionInfo(string.Empty, commit);
    }

    /// <summary>"2.14.0 → 2.15.0", "2.15.0 (unchanged)", "a1b2c3d → e4f5a6b"; empty when nothing is known.</summary>
    public static string Change(RepoVersionInfo before, RepoVersionInfo after)
    {
        if (after.IsEmpty) return string.Empty;
        if (before.IsEmpty) return after.Display;
        if (before.Version.Length > 0 && after.Version.Length > 0)
            return string.Equals(before.Version, after.Version, StringComparison.OrdinalIgnoreCase)
                ? $"{after.Version} (unchanged)"
                : $"{before.Version} → {after.Version}";
        if (before.Display == after.Display) return $"{after.Display} (unchanged)";
        return $"{before.Display} → {after.Display}";
    }

    private static (string Version, string Source)? Find(string repo)
    {
        if (FromToolJson(repo) is { } t) return (t, "devpilot.tool.json");
        if (FromDotnet(repo) is { } d) return d;
        if (Text(Path.Combine(repo, "pyproject.toml")) is { } toml && Section(toml, "project", "tool.poetry") is { } tv) return (tv, "pyproject.toml");
        if (FromPython(repo) is { } p) return p;
        if (FromPackageJson(repo) is { } j) return (j, "package.json");
        foreach (var name in new[] { "VERSION", "version.txt" })
            if (Text(Path.Combine(repo, name)) is { } v && Clean(v.Split('\n')[0]) is { } line) return (line, name);
        return null;
    }

    private static string? FromToolJson(string repo) => JsonVersion(Path.Combine(repo, "devpilot.tool.json"));

    private static string? FromPackageJson(string repo) => JsonVersion(Path.Combine(repo, "package.json"));

    private static string? JsonVersion(string file)
    {
        if (Text(file) is not { } text) return null;
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Name.Equals("version", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                    return Clean(p.Value.GetString());
        }
        catch (JsonException) { }
        return null;
    }

    /// <summary>Directory.Build.props first, then the csproj files at the top and one folder down (apps before libraries).</summary>
    private static (string, string)? FromDotnet(string repo)
    {
        var props = Path.Combine(repo, "Directory.Build.props");
        if (Text(props) is { } pt && XmlValue(pt) is { } pv) return (pv, "Directory.Build.props");

        var projects = Files(repo, "*.csproj").Concat(Subdirs(repo).SelectMany(d => Files(d, "*.csproj"))).ToList();
        (string, string)? library = null;
        foreach (var proj in projects)
        {
            if (Text(proj) is not { } text || XmlValue(text) is not { } v) continue;
            var source = Path.GetRelativePath(repo, proj).Replace('\\', '/');
            if (AppProject.IsMatch(text)) return (v, source);
            library ??= (v, source);
        }
        return library;
    }

    private static string? XmlValue(string text) =>
        XmlVersion.Match(text) is { Success: true } m ? Clean(m.Groups[1].Value)
        : XmlVersionPrefix.Match(text) is { Success: true } p ? Clean(p.Groups[1].Value) : null;

    /// <summary>The version line inside one of the given TOML tables ([project] or [tool.poetry]).</summary>
    private static string? Section(string toml, params string[] tables)
    {
        string? current = null;
        foreach (var raw in toml.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { current = line.Trim('[', ']', ' '); continue; }
            if (current is null || !tables.Contains(current, StringComparer.OrdinalIgnoreCase)) continue;
            if (TomlVersion.Match(line) is { Success: true } m) return Clean(m.Groups[1].Value);
        }
        return null;
    }

    /// <summary>version.py or a package __init__.py at the top, one folder down, or under src/.</summary>
    private static (string, string)? FromPython(string repo)
    {
        var dirs = new List<string> { repo };
        dirs.AddRange(Subdirs(repo).Where(d => !Path.GetFileName(d).Equals("src", StringComparison.OrdinalIgnoreCase)));
        var src = Path.Combine(repo, "src");
        if (Directory.Exists(src)) dirs.AddRange(Subdirs(src));
        foreach (var name in new[] { "version.py", "__init__.py", "_version.py" })
            foreach (var dir in dirs)
            {
                var file = Path.Combine(dir, name);
                if (Text(file) is { } text && PyVersion.Match(text) is { Success: true } m && Clean(m.Groups[1].Value) is { } v)
                    return (v, Path.GetRelativePath(repo, file).Replace('\\', '/'));
            }
        return null;
    }

    private static IEnumerable<string> Subdirs(string dir)
    {
        try
        {
            return Directory.EnumerateDirectories(dir)
                .Where(d => Path.GetFileName(d) is { Length: > 0 } n && !n.StartsWith('.') && !SkippedDirs.Contains(n))
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase).Take(40).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static IEnumerable<string> Files(string dir, string pattern)
    {
        try { return Directory.EnumerateFiles(dir, pattern).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Take(20).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static string? Text(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > MaxFileBytes) return null;
            return File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    /// <summary>A plausible version text (short, one line, starts with a digit or "v"), else null.</summary>
    private static string? Clean(string? raw)
    {
        var v = (raw ?? string.Empty).Trim().Trim('"', '\'');
        if (v.Length == 0 || v.Length > 40 || v.Any(char.IsWhiteSpace)) return null;
        if (!char.IsDigit(v[0]) && !((v[0] is 'v' or 'V') && v.Length > 1 && char.IsDigit(v[1]))) return null;
        return v;
    }

    // ── Commit from the .git files ─────────────────────────────────────
    /// <summary>Short hash of HEAD read from the .git files (loose ref, packed-refs or detached HEAD); empty if unknown.</summary>
    public static string ReadCommit(string repo)
    {
        try
        {
            if (RepoScanner.GitDir(repo) is not { } gitDir) return string.Empty;
            var head = Path.Combine(gitDir, "HEAD");
            if (!File.Exists(head)) return string.Empty;
            var text = File.ReadAllText(head).Trim();
            if (!text.StartsWith("ref:", StringComparison.Ordinal)) return Short(text);
            var refName = text["ref:".Length..].Trim();
            // A worktree keeps its refs in the common git dir.
            foreach (var dir in new[] { gitDir, CommonDir(gitDir) }.Where(d => d is not null).Distinct())
            {
                var loose = Path.Combine(dir!, refName.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(loose)) return Short(File.ReadAllText(loose).Trim());
                var packed = Path.Combine(dir!, "packed-refs");
                if (!File.Exists(packed)) continue;
                foreach (var line in File.ReadLines(packed))
                {
                    var parts = line.Split(' ', 2);
                    if (parts.Length == 2 && parts[1].Trim() == refName) return Short(parts[0]);
                }
            }
            return string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return string.Empty; }
    }

    private static string? CommonDir(string gitDir)
    {
        var file = Path.Combine(gitDir, "commondir");
        if (!File.Exists(file)) return null;
        var target = File.ReadAllText(file).Trim();
        return Path.IsPathRooted(target) ? target : Path.GetFullPath(Path.Combine(gitDir, target));
    }

    private static string Short(string hash) =>
        hash.Length >= ShortCommit && hash.Take(ShortCommit).All(Uri.IsHexDigit) ? hash[..ShortCommit].ToLowerInvariant() : string.Empty;
}
