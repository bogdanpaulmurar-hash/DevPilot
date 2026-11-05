using DevPilot.Core.Launch;
using DevPilot.Models;

namespace DevPilot.Core.Repos;

public enum RepoProjectKind { Unknown, Python, CSharp }

/// <summary>
/// What "Update" does for one repository: which DevPilot tools run from it (they are closed and started again),
/// and how it is installed. <see cref="Available"/> is false when there is nothing to do beyond a pull.
/// </summary>
public sealed record RepoUpdatePlan(
    RepoProjectKind Kind,
    RepoCommand? Install,
    string InstallText,
    IReadOnlyList<ToolEntry> Tools,
    bool Available,
    string Note)
{
    public static RepoUpdatePlan None(string note = "") => new(RepoProjectKind.Unknown, null, string.Empty, [], false, note);

    /// <summary>"C# project · install: dotnet build … · closes and restarts: ScreenPilot" for the editor.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Kind != RepoProjectKind.Unknown) parts.Add(Kind == RepoProjectKind.CSharp ? "C# project" : "Python project");
            parts.Add(Install is null ? "no install step found" : "install: " + InstallText);
            if (Tools.Count > 0) parts.Add("closes and restarts: " + string.Join(", ", Tools.Select(t => t.Name)));
            if (Note.Length > 0) parts.Add(Note);
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>Works out the update of a repository from its files and the tools DevPilot manages. Reads names only.</summary>
public static class RepoUpdatePlanner
{
    private const int MaxDepth = 2;
    private const int MaxDirectories = 400;

    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", ".vscode", "node_modules", "bin", "obj", "packages", "__pycache__", ".venv", "venv", "env",
        "site-packages", "TestResults", "dist",
    };

    /// <summary>Install scripts, best first. A script wins over any generated command.</summary>
    private static readonly string[] InstallScripts =
    [
        "install.bat", "install.cmd", "install.ps1",
        @"scripts\install.cmd", @"scripts\install.bat", @"scripts\install.ps1",
    ];

    public static RepoUpdatePlan Plan(string repo, RepoConfig? cfg, IEnumerable<ToolEntry> allTools)
    {
        if (string.IsNullOrWhiteSpace(repo) || !Directory.Exists(repo)) return RepoUpdatePlan.None();
        if (IsDevPilotRepo(repo))
            return RepoUpdatePlan.None("DevPilot updates itself: close it, then run scripts\\install.cmd");
        if (cfg is { UpdateEnabled: false }) return RepoUpdatePlan.None("Update is switched off");

        var kind = Detect(repo);
        var tools = ToolsInRepo(repo, allTools);
        RepoCommand? install;
        string text;
        if (!string.IsNullOrWhiteSpace(cfg?.InstallCommand))
        {
            text = cfg!.InstallCommand.Trim();
            install = new RepoCommand { Name = "Install", CommandLine = text, Mode = RepoCommandMode.Hidden };
        }
        else install = DetectInstall(repo, kind, out text);

        return new RepoUpdatePlan(kind, install, text, tools, install is not null || tools.Count > 0, string.Empty);
    }

    /// <summary>This checkout is DevPilot itself: updating it from inside would replace the running program.</summary>
    public static bool IsDevPilotRepo(string repo) =>
        File.Exists(Path.Combine(repo, "DevPilot.sln"));

    public static RepoProjectKind Detect(string repo)
    {
        if (HasFile(repo, "*.sln", 0) || HasFile(repo, "*.csproj", MaxDepth)) return RepoProjectKind.CSharp;
        if (File.Exists(Path.Combine(repo, "pyproject.toml")) || File.Exists(Path.Combine(repo, "requirements.txt"))
            || File.Exists(Path.Combine(repo, "setup.py")) || HasFile(repo, "*.py", 0) || HasFile(repo, "*.pyw", 0)
            || HasFile(repo, "__main__.py", MaxDepth))
            return RepoProjectKind.Python;
        return RepoProjectKind.Unknown;
    }

    /// <summary>The install command and a short text for it, or null when none can be found.</summary>
    public static RepoCommand? DetectInstall(string repo, RepoProjectKind kind, out string text)
    {
        foreach (var rel in InstallScripts)
        {
            if (ExistingFile(repo, rel) is not { } full) continue;
            text = Path.GetRelativePath(repo, full);
            var line = full.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)
                ? $"powershell -NoProfile -ExecutionPolicy Bypass -File {CommandLineText.Quote(full)}"
                : CommandLineText.Quote(full);
            return new RepoCommand { Name = "Install", CommandLine = line, Mode = RepoCommandMode.Hidden };
        }

        if (kind == RepoProjectKind.CSharp && FindBuildTarget(repo) is { } target)
        {
            var rel = Path.GetRelativePath(repo, target);
            text = $"dotnet build {rel} -c Release";
            return new RepoCommand
            {
                Name = "Install",
                CommandLine = $"dotnet build {CommandLineText.Quote(target)} -c Release -nologo -v:minimal",
                Mode = RepoCommandMode.Hidden,
            };
        }

        if (kind == RepoProjectKind.Python)
        {
            var venv = Path.Combine(repo, ".venv", "Scripts", "python.exe");
            var req = Path.Combine(repo, "requirements.txt");
            if (File.Exists(venv) && File.Exists(req))
            {
                text = "pip install -r requirements.txt (in .venv)";
                return new RepoCommand
                {
                    Name = "Install",
                    CommandLine = $"{CommandLineText.Quote(venv)} -m pip install -r requirements.txt",
                    Mode = RepoCommandMode.Hidden,
                };
            }
        }
        text = string.Empty;
        return null;
    }

    /// <summary>The file with the name as stored on disk (the list above is lower case), or null.</summary>
    private static string? ExistingFile(string repo, string relative)
    {
        try
        {
            var full = Path.Combine(repo, relative);
            var dir = Path.GetDirectoryName(full);
            if (dir is null || !Directory.Exists(dir)) return null;
            return Directory.EnumerateFiles(dir, Path.GetFileName(full)).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The solution in the root, else the shallowest project file.</summary>
    public static string? FindBuildTarget(string repo)
    {
        try
        {
            var sln = Directory.EnumerateFiles(repo, "*.sln").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (sln is not null) return sln;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return Enumerate(repo, "*.csproj", MaxDepth).OrderBy(f => f.Count(c => c == Path.DirectorySeparatorChar))
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    /// <summary>Tools DevPilot manages that start a program from inside the repository.</summary>
    public static IReadOnlyList<ToolEntry> ToolsInRepo(string repo, IEnumerable<ToolEntry> tools) =>
        tools.Where(t => t.IsProcessLike && (IsUnder(repo, PathTokens.Expand(t.WorkingDirectory, t))
                                             || IsUnder(repo, PathTokens.Expand(t.Path, t)))).ToList();

    public static bool IsUnder(string folder, string path)
    {
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(path)) return false;
        var f = RepoScanner.Normalize(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var p = RepoScanner.Normalize(path.Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasFile(string root, string pattern, int depth) => Enumerate(root, pattern, depth).Any();

    private static IEnumerable<string> Enumerate(string root, string pattern, int depth)
    {
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((root, 0));
        int visited = 0;
        var found = new List<string>();
        while (queue.Count > 0 && visited++ < MaxDirectories)
        {
            var (dir, d) = queue.Dequeue();
            try
            {
                found.AddRange(Directory.EnumerateFiles(dir, pattern));
                if (d >= depth) continue;
                foreach (var child in Directory.EnumerateDirectories(dir))
                    if (!Skipped.Contains(Path.GetFileName(child))) queue.Enqueue((child, d + 1));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return found;
    }
}

/// <summary>Update = pull, then close/install/start. Reinstall = the same without the pull.</summary>
public enum RepoUpdateMode { Full, ReinstallOnly }
