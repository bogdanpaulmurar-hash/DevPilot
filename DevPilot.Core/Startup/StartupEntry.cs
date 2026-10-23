using DevPilot.Core.Launch;
using DevPilot.Core.Presets;
using DevPilot.Models;

namespace DevPilot.Core.Startup;

/// <summary>One Windows autostart entry (registry Run value or Startup-folder shortcut).</summary>
public sealed record StartupEntry(string Location, string Name, string Command, bool CanRemove = true);

public sealed record StartupConflict(StartupEntry Entry, ToolEntry Tool, string Reason);

/// <summary>Finds autostart entries that belong to tools DevPilot manages.</summary>
public static class StartupMatcher
{
    public const string DevPilotName = "DevPilot";

    public static IReadOnlyList<StartupConflict> FindConflicts(IEnumerable<StartupEntry> entries, IEnumerable<ToolEntry> tools)
    {
        var toolList = tools.Where(t => !t.IsSeparator).ToList();
        var result = new List<StartupConflict>();
        foreach (var e in entries)
        {
            if (string.Equals(e.Name, DevPilotName, StringComparison.OrdinalIgnoreCase)) continue;
            var match = Match(e, toolList);
            if (match is not null) result.Add(match);
        }
        return result;
    }

    public static StartupConflict? Match(StartupEntry e, IReadOnlyList<ToolEntry> tools)
    {
        foreach (var tool in tools)
        {
            var preset = ToolPresets.Recognise(tool);
            if (preset is not null && preset.StartupNames.Any(n => NameEquals(e.Name, n)))
                return new StartupConflict(e, tool, $"Known autostart name of {preset.Name}");
        }
        foreach (var tool in tools)
        {
            if (NameEquals(e.Name, tool.Name))
                return new StartupConflict(e, tool, "Same name as the tool");
            if (!tool.IsProcessLike) continue;
            var target = PathTokens.Expand(tool.Path, tool).Trim('"');
            if (target.Length > 3 && e.Command.Contains(target, StringComparison.OrdinalIgnoreCase))
                return new StartupConflict(e, tool, "Starts the same file");
            if (!string.IsNullOrWhiteSpace(tool.PythonModule)
                && e.Command.Contains("-m " + tool.PythonModule, StringComparison.OrdinalIgnoreCase))
                return new StartupConflict(e, tool, "Starts the same Python module");
            var file = Path.GetFileName(target);
            if (file.Length > 4 && !IsGenericHost(file) && e.Command.Contains(file, StringComparison.OrdinalIgnoreCase))
                return new StartupConflict(e, tool, "Starts the same program");
        }
        return null;
    }

    private static bool IsGenericHost(string file) =>
        file.Equals("python.exe", StringComparison.OrdinalIgnoreCase) || file.Equals("pythonw.exe", StringComparison.OrdinalIgnoreCase)
        || file.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) || file.Equals("wscript.exe", StringComparison.OrdinalIgnoreCase)
        || file.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase);

    private static bool NameEquals(string a, string b)
    {
        static string N(string s) => new string(Path.GetFileNameWithoutExtension(s).Where(char.IsLetterOrDigit).ToArray());
        return N(a).Equals(N(b), StringComparison.OrdinalIgnoreCase) && N(a).Length > 0;
    }
}
