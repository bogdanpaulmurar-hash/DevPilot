using System.Text;
using System.Text.Json;
using DevPilot.Core.Config;
using DevPilot.Core.Launch;
using DevPilot.Models;

namespace DevPilot.Core.Backup;

/// <summary>What a restore would change, shown to the user before anything happens.</summary>
public sealed class RestorePreview
{
    public BackupManifest Manifest { get; init; } = new();
    public int ToolsAdded { get; set; }
    public int ToolsRemoved { get; set; }
    public int ToolsChanged { get; set; }
    public int GroupsAdded { get; set; }
    public int GroupsRemoved { get; set; }
    public int GroupsChanged { get; set; }
    /// <summary>Other settings that differ (theme, hub look, keep-awake, monitor, shortcuts).</summary>
    public List<string> OtherChanges { get; } = [];
    /// <summary>Paths rewritten from the backup's user folder to this PC's user folder.</summary>
    public int AdaptedPaths { get; set; }
    /// <summary>Names of tools whose program, script or folder does not exist on this PC (kept as they are).</summary>
    public List<string> MissingPaths { get; } = [];
    public bool FromOtherUserFolder { get; set; }

    public bool HasChanges => ToolsAdded + ToolsRemoved + ToolsChanged + GroupsAdded + GroupsRemoved + GroupsChanged + OtherChanges.Count > 0;

    /// <summary>Plain text summary for the confirmation dialog.</summary>
    public string Describe()
    {
        var sb = new StringBuilder();
        var m = Manifest;
        sb.AppendLine($"Backup from {m.CreatedAt:yyyy-MM-dd HH:mm}" + (m.Machine.Length > 0 ? $" ({m.Machine})" : string.Empty)
                      + (m.AppVersion.Length > 0 ? $", DevPilot {m.AppVersion}" : string.Empty) + ".");
        sb.AppendLine($"{m.Tools} tool(s), {m.Groups} group(s), {m.Logos.Count} logo(s).");
        sb.AppendLine();
        if (!HasChanges) sb.AppendLine("It is the same as your current configuration.");
        else
        {
            sb.AppendLine("Compared with now:");
            if (ToolsAdded + ToolsRemoved + ToolsChanged > 0)
                sb.AppendLine($"• Tools: {ToolsAdded} come back, {ToolsRemoved} go away, {ToolsChanged} change");
            if (GroupsAdded + GroupsRemoved + GroupsChanged > 0)
                sb.AppendLine($"• Groups: {GroupsAdded} come back, {GroupsRemoved} go away, {GroupsChanged} change");
            if (OtherChanges.Count > 0) sb.AppendLine("• Also different: " + string.Join(", ", OtherChanges));
        }
        if (AdaptedPaths > 0) sb.AppendLine($"• {AdaptedPaths} path(s) adapted to this PC's user folder");
        else if (FromOtherUserFolder) sb.AppendLine("• Made under another user folder; paths are kept as they are (see the Backup setting)");
        if (MissingPaths.Count > 0)
        {
            var names = string.Join(", ", MissingPaths.Take(6)) + (MissingPaths.Count > 6 ? $" and {MissingPaths.Count - 6} more" : string.Empty);
            sb.AppendLine($"• Not found on this PC (kept, fix later): {names}");
        }
        sb.AppendLine();
        sb.AppendLine("Start with Windows, the autostart entries DevPilot took over, the window size and the backup settings stay as they are on this PC.");
        sb.Append("A backup of your current configuration is made first.");
        return sb.ToString();
    }
}

/// <summary>Builds the configuration a restore writes, and the preview of what it changes.</summary>
public static class BackupRestore
{
    /// <summary>
    /// The backup's configuration with this PC's local settings kept (startup, window, backup settings),
    /// picked logos pointed at their restored copies (<paramref name="logoMap"/>: old IconPath → new file) and,
    /// if asked, paths under the old user folder rewritten to the new one.
    /// </summary>
    public static AppSettings Merge(AppSettings current, AppSettings fromBackup, IReadOnlyDictionary<string, string>? logoMap,
        string? oldUserFolder, string? newUserFolder, bool adaptUserFolder, out int adaptedPaths)
    {
        var result = Clone(fromBackup);
        result.Startup = Clone(current.Startup);
        result.Window = Clone(current.Window);
        result.Backup = Clone(current.Backup);

        if (logoMap is { Count: > 0 })
        {
            foreach (var t in result.Tools)
                if (!string.IsNullOrWhiteSpace(t.IconPath) && logoMap.TryGetValue(t.IconPath.Trim(), out var local))
                    t.IconPath = local;
            foreach (var g in result.Groups)
                if (!string.IsNullOrWhiteSpace(g.IconPath) && logoMap.TryGetValue(g.IconPath.Trim(), out var local))
                    g.IconPath = local;
        }

        adaptedPaths = adaptUserFolder && !string.IsNullOrWhiteSpace(oldUserFolder) && !string.IsNullOrWhiteSpace(newUserFolder)
            ? UserFolderRemap.Apply(result, oldUserFolder, newUserFolder)
            : 0;
        Normalizer.Normalize(result);
        return result;
    }

    /// <param name="pathExists">True when a file or folder exists (only rooted local paths are checked).</param>
    public static RestorePreview Preview(AppSettings current, AppSettings merged, BackupManifest manifest, int adaptedPaths,
        string? currentUserFolder, Func<string, bool> pathExists)
    {
        var p = new RestorePreview
        {
            Manifest = manifest,
            AdaptedPaths = adaptedPaths,
            FromOtherUserFolder = !string.IsNullOrWhiteSpace(manifest.UserFolder) && !string.IsNullOrWhiteSpace(currentUserFolder)
                && !manifest.UserFolder.TrimEnd('\\', '/').Equals(currentUserFolder.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase),
        };

        (p.ToolsAdded, p.ToolsRemoved, p.ToolsChanged) = Diff(merged.Tools, current.Tools, t => t.Id);
        (p.GroupsAdded, p.GroupsRemoved, p.GroupsChanged) = Diff(merged.Groups, current.Groups, g => g.Id);

        if (merged.Theme != current.Theme || !string.Equals(merged.Accent, current.Accent, StringComparison.OrdinalIgnoreCase))
            p.OtherChanges.Add("theme");
        if (Json(merged.Hub) != Json(current.Hub)) p.OtherChanges.Add("hub look");
        if (Json(merged.KeepAwake) != Json(current.KeepAwake)) p.OtherChanges.Add("keep-awake");
        if (Json(merged.Monitor) != Json(current.Monitor)) p.OtherChanges.Add("monitor");
        if (Json(merged.Hotkeys) != Json(current.Hotkeys)) p.OtherChanges.Add("shortcuts");

        foreach (var t in merged.Tools)
            if (CheckedPath(t) is { } path && !SafeExists(pathExists, path))
                p.MissingPaths.Add(t.Name.Length > 0 ? t.Name : path);
        return p;
    }

    /// <summary>The local path a tool needs, or null when there is nothing to check (URL, command, PATH lookup, network share…).</summary>
    public static string? CheckedPath(ToolEntry t)
    {
        if (t.IsSeparator || string.IsNullOrWhiteSpace(t.Path)) return null;
        bool fileLike = t.Kind is ToolKind.Executable or ToolKind.PowerShell or ToolKind.Batch or ToolKind.VBScript
            or ToolKind.Document or ToolKind.Folder
            || (t.Kind == ToolKind.Python && string.IsNullOrWhiteSpace(t.PythonModule));
        if (!fileLike) return null;
        string path;
        try { path = PathTokens.Expand(t.Path, t).Trim().Trim('"'); }
        catch (ArgumentException) { return null; }
        if (path.Length < 3 || path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) return null;
        // Only "C:\…" style paths: a bare "notepad.exe" is found through PATH.
        return char.IsLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/' ? path : null;
    }

    private static bool SafeExists(Func<string, bool> exists, string path)
    {
        try { return exists(path); }
        catch (Exception) { return true; }
    }

    private static (int Added, int Removed, int Changed) Diff<T>(IEnumerable<T> next, IEnumerable<T> now, Func<T, string> id)
    {
        var a = next.GroupBy(id).ToDictionary(g => g.Key, g => Json(g.First()));
        var b = now.GroupBy(id).ToDictionary(g => g.Key, g => Json(g.First()));
        int added = a.Keys.Count(k => !b.ContainsKey(k));
        int removed = b.Keys.Count(k => !a.ContainsKey(k));
        int changed = a.Count(kv => b.TryGetValue(kv.Key, out var other) && other != kv.Value);
        return (added, removed, changed);
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, ConfigSerializer.Options);

    public static T Clone<T>(T value) where T : class =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, ConfigSerializer.Options), ConfigSerializer.Options)!;
}
