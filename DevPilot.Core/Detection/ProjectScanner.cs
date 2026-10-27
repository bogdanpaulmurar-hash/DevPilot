using System.Text.Json;
using DevPilot.Core.Presets;
using DevPilot.Models;

namespace DevPilot.Core.Detection;

/// <summary>What <see cref="ProjectScanner"/> found in a folder: how to start it and why.</summary>
public sealed record ProjectDetection(
    ToolKind Kind,
    string Name,
    string Path = "",
    string WorkingDirectory = "",
    string Arguments = "",
    string PythonModule = "",
    string Interpreter = "",
    bool HideWindow = false,
    string ManifestPath = "",
    string Summary = "",
    IReadOnlyList<string>? Notes = null)
{
    public IReadOnlyList<string> NoteList => Notes ?? [];

    /// <summary>Builds the tool entry: detected fields first, then the tool's own manifest or a known preset on top.</summary>
    public ToolEntry ToTool()
    {
        var tool = new ToolEntry
        {
            Name = Name, Kind = Kind, Path = Path, WorkingDirectory = WorkingDirectory, Arguments = Arguments,
            PythonModule = PythonModule, Interpreter = Interpreter, HideWindow = HideWindow,
            Description = Summary,
        };
        if (ManifestPath.Length > 0)
        {
            try { ToolManifest.Load(ManifestPath).ApplyTo(tool, overwrite: false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        else if (ToolPresets.Recognise(tool) is { } preset && preset.Kind == Kind)
            ToolPresets.Apply(tool, preset, overwriteUserFields: false);
        return tool;
    }
}

/// <summary>
/// Looks at a project folder (a clone of a repository) and works out how to start it.
/// Cheap by design: it reads names only, a few levels deep, never inside build/dependency folders,
/// and stops after <see cref="MaxEntries"/> entries, so a big C# solution does not slow DevPilot down.
/// </summary>
public static class ProjectScanner
{
    public const int MaxEntries = 1500;
    private const int MaxDepth = 3;

    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", ".vscode", "node_modules", "obj", "packages", "__pycache__", ".venv", "venv", "env",
        ".tox", ".mypy_cache", ".pytest_cache", "site-packages", "dist", "TestResults", "tests", "test", "docs",
    };

    private static readonly string[] PythonEntryNames =
        ["main.pyw", "main.py", "app.pyw", "app.py", "run.pyw", "run.py", "__main__.py"];
    private static readonly string[] ScriptEntryNames =
        ["start", "run", "launch"];

    /// <summary>Never throws for unreadable folders; returns a plain folder entry when nothing is recognised.</summary>
    public static ProjectDetection Scan(string folder)
    {
        folder = folder.TrimEnd('\\', '/');
        string name = System.IO.Path.GetFileName(folder) is { Length: > 0 } n ? n : folder;
        List<string> files;
        try { files = Collect(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fallback(folder, name, "The folder could not be read.");
        }

        string? Find(string fileName, int maxDepth = MaxDepth) =>
            files.Where(f => string.Equals(System.IO.Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase)
                             && Depth(folder, f) <= maxDepth)
                 .OrderBy(f => Depth(folder, f)).FirstOrDefault();

        var manifest = Find(ToolManifest.FileName, 1);
        string manifestNote = manifest is null ? "" : "Options come from devpilot.tool.json. ";

        // Python: a package with __main__.py (run with -m), else an entry script.
        bool isPython = files.Any(f => f.EndsWith(".py", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".pyw", StringComparison.OrdinalIgnoreCase))
                        && (Find("pyproject.toml", 1) is not null || Find("requirements.txt", 1) is not null || Find("setup.py", 1) is not null
                            || PythonEntryNames.Any(e => Find(e, 1) is not null) || !files.Any(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)));
        if (isPython)
        {
            string interpreter = Interpreter(folder);
            var pkg = files.Where(f => string.Equals(System.IO.Path.GetFileName(f), "__main__.py", StringComparison.OrdinalIgnoreCase)
                                       && Depth(folder, f) is 2 or 3
                                       && System.IO.Path.GetDirectoryName(f) is { } d && !Skipped.Contains(System.IO.Path.GetFileName(d)))
                           .OrderBy(f => Depth(folder, f)).FirstOrDefault();
            if (pkg is not null && System.IO.Path.GetDirectoryName(pkg) is { } pkgDir)
            {
                string module = System.IO.Path.GetFileName(pkgDir);
                string root = System.IO.Path.GetDirectoryName(pkgDir) ?? folder;
                return new ProjectDetection(ToolKind.Python, name, WorkingDirectory: root, PythonModule: module, Interpreter: interpreter,
                    HideWindow: true, ManifestPath: manifest ?? "",
                    Summary: "Python module", Notes: Notes(manifestNote + $"Python package '{module}' (python -m {module}).", interpreter));
            }
            foreach (var entry in PythonEntryNames.Append(name + ".pyw").Append(name + ".py"))
            {
                if (Find(entry, 2) is { } script)
                    return new ProjectDetection(ToolKind.Python, name, Path: script, Interpreter: interpreter,
                        HideWindow: script.EndsWith(".pyw", StringComparison.OrdinalIgnoreCase),
                        ManifestPath: manifest ?? "", Summary: "Python script",
                        Notes: Notes(manifestNote + $"Python script {System.IO.Path.GetFileName(script)}.", interpreter));
            }
        }

        // .NET: a built program if there is one, else dotnet run.
        var csproj = files.Where(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) && IsProgram(f))
                          .OrderBy(f => Depth(folder, f)).ThenBy(f => f).FirstOrDefault();
        if (csproj is not null)
        {
            string project = System.IO.Path.GetFileNameWithoutExtension(csproj);
            var built = BuiltProgram(System.IO.Path.GetDirectoryName(csproj)!, project);
            if (built is not null)
                return new ProjectDetection(ToolKind.Executable, name, Path: built, ManifestPath: manifest ?? "", Summary: "C# program",
                    Notes: [manifestNote + $"C# project {project}: using the built program. Build again after changes (dotnet build -c Release)."]);
            return new ProjectDetection(ToolKind.ShellCommand, name,
                Path: "dotnet", Arguments: $"run -c Release --project \"{csproj}\"", WorkingDirectory: folder, HideWindow: true,
                ManifestPath: manifest ?? "", Summary: "C# program",
                Notes: [manifestNote + $"C# project {project} is not built yet: starts with dotnet run (needs the .NET SDK)."]);
        }

        // Node.
        if (Find("package.json", 1) is { } pkgJson && HasStartScript(pkgJson))
            return new ProjectDetection(ToolKind.ShellCommand, name, Path: "npm", Arguments: "start", WorkingDirectory: folder,
                HideWindow: true, ManifestPath: manifest ?? "", Summary: "Node.js app",
                Notes: [manifestNote + "package.json has a start script: npm start."]);

        // Launcher scripts and programs in the folder itself.
        foreach (var baseName in ScriptEntryNames.Append(name))
            foreach (var (ext, kind) in new[] { (".cmd", ToolKind.Batch), (".bat", ToolKind.Batch), (".ps1", ToolKind.PowerShell), (".vbs", ToolKind.VBScript) })
                if (Find(baseName + ext, 1) is { } launcher)
                    return new ProjectDetection(kind, name, Path: launcher, ManifestPath: manifest ?? "", Summary: "Script",
                        Notes: [manifestNote + $"Launcher script {System.IO.Path.GetFileName(launcher)}."]);

        var exes = files.Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && Depth(folder, f) <= 1).ToList();
        var exe = exes.FirstOrDefault(f => string.Equals(System.IO.Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase))
                  ?? (exes.Count == 1 ? exes[0] : null);
        if (exe is not null)
            return new ProjectDetection(ToolKind.Executable, name, Path: exe, ManifestPath: manifest ?? "", Summary: "Program",
                Notes: [manifestNote + $"Program {System.IO.Path.GetFileName(exe)}."]);

        return Fallback(folder, name, manifestNote + "No program, script or project found: added as a folder.", manifest);
    }

    private static ProjectDetection Fallback(string folder, string name, string note, string? manifest = null) =>
        new(ToolKind.Folder, name, Path: folder, ManifestPath: manifest ?? "", Summary: "Folder", Notes: [note]);

    private static IReadOnlyList<string> Notes(string note, string interpreter) =>
        interpreter.Length > 0 ? [note, $"Using the project's virtual environment: {interpreter}."] : [note];

    private static string Interpreter(string folder)
    {
        foreach (var venv in new[] { ".venv", "venv", "env" })
        {
            var pythonw = System.IO.Path.Combine(folder, venv, "Scripts", "pythonw.exe");
            if (File.Exists(pythonw)) return pythonw;
        }
        return string.Empty;
    }

    /// <summary>True for a project that builds a program (WinExe / Exe), not a library or test project.</summary>
    private static bool IsProgram(string csproj)
    {
        try
        {
            var text = File.ReadAllText(csproj);
            if (text.Contains("<IsTestProject>true", StringComparison.OrdinalIgnoreCase)) return false;
            return text.Contains("<OutputType>WinExe", StringComparison.OrdinalIgnoreCase)
                   || text.Contains("<OutputType>Exe", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static string? BuiltProgram(string projectDir, string project)
    {
        foreach (var config in new[] { "Release", "Debug" })
        {
            var bin = System.IO.Path.Combine(projectDir, "bin", config);
            if (!Directory.Exists(bin)) continue;
            try
            {
                var match = Directory.EnumerateFiles(bin, project + ".exe", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (match is not null) return match;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    private static bool HasStartScript(string packageJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(packageJson));
            return doc.RootElement.TryGetProperty("scripts", out var scripts) && scripts.TryGetProperty("start", out _);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

    private static int Depth(string root, string file)
    {
        var rel = System.IO.Path.GetRelativePath(root, file);
        return rel.Count(c => c is '\\' or '/') + 1;
    }

    /// <summary>File names up to <see cref="MaxDepth"/> levels down, skipping dependency and build folders.</summary>
    private static List<string> Collect(string root)
    {
        var result = new List<string>();
        int seen = 0;
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((root, 1));
        while (queue.Count > 0 && seen < MaxEntries)
        {
            var (dir, depth) = queue.Dequeue();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(dir).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var entry in entries)
            {
                if (++seen > MaxEntries) break;
                if (Directory.Exists(entry))
                {
                    if (depth < MaxDepth && !Skipped.Contains(System.IO.Path.GetFileName(entry))) queue.Enqueue((entry, depth + 1));
                }
                else result.Add(entry);
            }
        }
        return result;
    }
}
