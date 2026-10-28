using System.Globalization;
using DevPilot.Core.Launch;
using DevPilot.Models;

namespace DevPilot.Core.Backup;

/// <summary>A logo file to copy into a backup.</summary>
public sealed record LogoToCopy(string Source, string Entry, IReadOnlyList<string> IconPaths);

/// <summary>A backup file found in the backup folder.</summary>
public sealed record BackupFileInfo(string Path, DateTime CreatedAt, bool BeforeRestore);

/// <summary>File names, rotation, scheduling and logo selection for backups (no I/O except the injected checks).</summary>
public static class BackupPlanner
{
    public const string Prefix = "DevPilot-backup-";
    public const string BeforeRestoreSuffix = "-before-restore";
    public const string SearchPattern = "DevPilot-backup-*.zip";
    /// <summary>Safety copies made before a restore that stay in the folder.</summary>
    public const int KeepBeforeRestore = 3;
    private const string Stamp = "yyyyMMdd-HHmmss";

    /// <summary>Only real image files are copied; .exe / .dll / .lnk logos are the program itself and stay where they are.</summary>
    public static readonly string[] ImageExtensions = [".ico", ".png", ".jpg", ".jpeg", ".bmp", ".gif"];

    public static string FileName(DateTime createdAt, bool beforeRestore = false) =>
        Prefix + createdAt.ToString(Stamp, CultureInfo.InvariantCulture) + (beforeRestore ? BeforeRestoreSuffix : string.Empty) + ".zip";

    public static bool TryParse(string path, out BackupFileInfo info)
    {
        info = new BackupFileInfo(path, default, false);
        var name = System.IO.Path.GetFileName(path);
        if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return false;
        var core = name[Prefix.Length..^4];
        bool safety = core.EndsWith(BeforeRestoreSuffix, StringComparison.OrdinalIgnoreCase);
        if (safety) core = core[..^BeforeRestoreSuffix.Length];
        if (!DateTime.TryParseExact(core, Stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var created))
            return false;
        info = new BackupFileInfo(path, created, safety);
        return true;
    }

    /// <summary>Backups to delete: everything beyond the newest <paramref name="keep"/> regular backups and the newest
    /// <see cref="KeepBeforeRestore"/> safety copies. Files that are not DevPilot backups are never returned.</summary>
    public static IReadOnlyList<string> ToDelete(IEnumerable<string> files, int keep)
    {
        keep = Math.Clamp(keep, 1, BackupSettings.MaxKeep);
        var parsed = new List<BackupFileInfo>();
        foreach (var f in files)
            if (TryParse(f, out var info)) parsed.Add(info);
        IEnumerable<string> Old(bool safety, int n) => parsed
            .Where(p => p.BeforeRestore == safety)
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Path, StringComparer.OrdinalIgnoreCase)
            .Skip(n)
            .Select(p => p.Path);
        return Old(false, keep).Concat(Old(true, KeepBeforeRestore)).ToList();
    }

    /// <summary>Whether an automatic backup should run now (called once, shortly after DevPilot starts).</summary>
    public static bool IsDue(BackupMode mode, DateTime? lastBackup, DateTime now) => mode switch
    {
        BackupMode.EveryStart => true,
        BackupMode.Daily => lastBackup is null || lastBackup.Value.Date < now.Date,
        _ => false,
    };

    public static bool IsImage(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        return ImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The picked logos (<see cref="ToolEntry.IconPath"/> and <see cref="ToolGroup.IconPath"/>) that are image files
    /// and exist, each once, with a unique entry name.
    /// </summary>
    public static IReadOnlyList<LogoToCopy> PlanLogos(AppSettings settings, Func<string, bool> fileExists)
    {
        var bySource = new Dictionary<string, (string Entry, List<string> Raw)>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groupLogos = (settings.Groups ?? []).Select(Hub.GroupHeaderLook.LogoCarrier).OfType<ToolEntry>();
        foreach (var tool in settings.Tools.Concat(groupLogos))
        {
            var raw = tool.IconPath?.Trim() ?? string.Empty;
            if (raw.Length == 0) continue;
            string full;
            try { full = PathTokens.Expand(raw, tool).Trim().Trim('"'); }
            catch (ArgumentException) { continue; }
            if (full.Length == 0 || !IsImage(full)) continue;
            if (bySource.TryGetValue(full, out var known))
            {
                if (!known.Raw.Contains(raw, StringComparer.Ordinal)) known.Raw.Add(raw);
                continue;
            }
            if (!fileExists(full)) continue;
            var entry = BackupManifest.LogosFolder + UniqueName(SafeFileName(full), used);
            bySource[full] = (entry, [raw]);
            order.Add(full);
        }
        return order.Select(s => new LogoToCopy(s, bySource[s].Entry, bySource[s].Raw)).ToList();
    }

    /// <summary>Name not yet in <paramref name="used"/> ("logo.png", "logo-2.png", …); adds it.</summary>
    public static string UniqueName(string name, ISet<string> used)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(name);
        var ext = System.IO.Path.GetExtension(name);
        var candidate = name;
        for (int i = 2; !used.Add(candidate); i++) candidate = $"{stem}-{i}{ext}";
        return candidate;
    }

    private static string SafeFileName(string path)
    {
        var name = path.Replace('/', '\\');
        int slash = name.LastIndexOf('\\');
        if (slash >= 0) name = name[(slash + 1)..];
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        name = new string(name.Select(c => invalid.Contains(c) || c is ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c).ToArray());
        return name.Length == 0 ? "logo.png" : name;
    }
}
