using DevPilot.Models;

namespace DevPilot.Core.Launch;

/// <summary>Expands %ENV% variables and DevPilot tokens such as {ToolDir}.</summary>
public static class PathTokens
{
    public static string ToolDirectory(ToolEntry tool)
    {
        if (!string.IsNullOrWhiteSpace(tool.WorkingDirectory)) return Expand(tool.WorkingDirectory, null);
        if (string.IsNullOrWhiteSpace(tool.Path)) return string.Empty;
        var p = Environment.ExpandEnvironmentVariables(tool.Path.Trim().Trim('"'));
        if (tool.IsFolderLike) return p;
        if (tool.Kind is ToolKind.Url or ToolKind.ShellCommand or ToolKind.Ssh or ToolKind.Wsl) return string.Empty;
        try { return System.IO.Path.GetDirectoryName(p) ?? string.Empty; }
        catch (ArgumentException) { return string.Empty; }
    }

    public static string Expand(string? value, ToolEntry? tool)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var s = value;
        if (s.Contains('{'))
        {
            s = Replace(s, "{Home}", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            s = Replace(s, "{Pictures}", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            s = Replace(s, "{Documents}", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            s = Replace(s, "{Desktop}", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            s = Replace(s, "{AppData}", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            s = Replace(s, "{LocalAppData}", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            if (tool is not null)
            {
                s = Replace(s, "{ToolDir}", ToolDirectory(tool));
                s = Replace(s, "{Path}", Environment.ExpandEnvironmentVariables(tool.Path));
                s = Replace(s, "{Name}", tool.Name);
            }
        }
        return Environment.ExpandEnvironmentVariables(s);
    }

    private static string Replace(string s, string token, string value) =>
        s.Replace(token, value, StringComparison.OrdinalIgnoreCase);
}
