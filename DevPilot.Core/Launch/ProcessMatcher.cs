using DevPilot.Models;

namespace DevPilot.Core.Launch;

/// <summary>Snapshot of one running process (filled by the infrastructure layer).</summary>
public sealed record ProcessInfo(int Id, string Name, string CommandLine, DateTime? StartTime = null);

/// <summary>Decides which running processes belong to a tool.</summary>
public static class ProcessMatcher
{
    private static readonly string[] PythonHosts = ["python", "pythonw", "py", "pyw"];

    /// <summary>The patterns used for a tool (explicit <see cref="MonitorOptions.ProcessMatch"/> or derived from its target).</summary>
    public static IReadOnlyList<string> Patterns(ToolEntry tool)
    {
        if (!string.IsNullOrWhiteSpace(tool.Monitor.ProcessMatch))
            return tool.Monitor.ProcessMatch.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        switch (tool.Kind)
        {
            case ToolKind.Executable:
                var exe = Path.GetFileName(Environment.ExpandEnvironmentVariables(tool.Path.Trim('"')));
                return string.IsNullOrEmpty(exe) ? [] : [exe];
            case ToolKind.Python:
                if (!string.IsNullOrWhiteSpace(tool.PythonModule)) return ["-m " + tool.PythonModule.Trim()];
                var script = Path.GetFileName(tool.Path.Trim('"'));
                return string.IsNullOrEmpty(script) ? [] : [script];
            case ToolKind.PowerShell:
            case ToolKind.Batch:
            case ToolKind.VBScript:
                var file = Path.GetFileName(tool.Path.Trim('"'));
                return string.IsNullOrEmpty(file) ? [] : [file];
            default:
                return [];
        }
    }

    public static bool CanTrack(ToolEntry tool) => tool.IsProcessLike && Patterns(tool).Count > 0;

    public static bool Matches(ToolEntry tool, ProcessInfo p)
    {
        foreach (var pattern in Patterns(tool))
            if (MatchesPattern(pattern, p, tool.Kind == ToolKind.Python)) return true;
        return false;
    }

    /// <summary>
    /// "name.exe" or a bare word without spaces/dots compares to the process name;
    /// anything else must appear in the command line (case-insensitive).
    /// For Python tools only python hosts are considered unless the pattern is an .exe name.
    /// </summary>
    public static bool MatchesPattern(string pattern, ProcessInfo p, bool pythonTool)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        var name = Path.GetFileNameWithoutExtension(p.Name);
        if (pattern.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return string.Equals(name, Path.GetFileNameWithoutExtension(pattern), StringComparison.OrdinalIgnoreCase);

        if (pythonTool && !PythonHosts.Contains(name, StringComparer.OrdinalIgnoreCase))
            return false;

        bool bareWord = pattern.IndexOfAny([' ', '.', '\\', '/', '-']) < 0;
        if (!pythonTool && bareWord && string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase))
            return true;
        return !string.IsNullOrEmpty(p.CommandLine)
               && p.CommandLine.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }
}
