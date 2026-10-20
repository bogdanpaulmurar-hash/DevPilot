using DevPilot.Core.Interfaces;
using DevPilot.Core.Startup;
using DevPilot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DevPilot.Infrastructure.Services;

/// <summary>
/// DevPilot's own autostart plus the "conflict guard": finds autostart entries of managed tools,
/// removes them with a backup (so DevPilot starts the tools instead) and can restore them.
/// </summary>
public sealed class StartupManager : IStartupManager
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string LocationHkcu = "HKCU-Run";
    public const string LocationHklm = "HKLM-Run";
    public const string LocationFolder = "StartupFolder";
    public const string ValueName = "DevPilot";

    private readonly ILogger<StartupManager> _logger;
    private readonly IConfigStore _config;

    public StartupManager(ILogger<StartupManager> logger, IConfigStore config)
    {
        _logger = logger;
        _config = config;
    }

    private static string ExePath => Environment.ProcessPath ?? string.Empty;

    public bool IsDevPilotRegistered
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string s && s.Contains(Path.GetFileName(ExePath), StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetDevPilotStartup(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enable) key.SetValue(ValueName, $"\"{ExePath}\" --autostart");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
        _logger.LogInformation("DevPilot autostart {State}", enable ? "on" : "off");
    }

    public IReadOnlyList<StartupEntry> ScanEntries()
    {
        var list = new List<StartupEntry>();
        ReadRun(Registry.CurrentUser, LocationHkcu, canRemove: true, list);
        ReadRun(Registry.LocalMachine, LocationHklm, canRemove: false, list);
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if (Directory.Exists(folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder).Where(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)))
            {
                var target = ShortcutTarget(file);
                list.Add(new StartupEntry(LocationFolder, Path.GetFileNameWithoutExtension(file),
                    string.IsNullOrEmpty(target) ? file : $"{target}  ({Path.GetFileName(file)})"));
            }
        }
        return list;
    }

    private static void ReadRun(RegistryKey hive, string location, bool canRemove, List<StartupEntry> list)
    {
        try
        {
            using var key = hive.OpenSubKey(RunKeyPath, writable: false);
            if (key is null) return;
            foreach (var name in key.GetValueNames())
                list.Add(new StartupEntry(location, name, key.GetValue(name)?.ToString() ?? string.Empty, canRemove));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
    }

    /// <summary>Resolves a .lnk target through the Windows Script Host (best effort).</summary>
    private static string ShortcutTarget(string lnk)
    {
        if (!lnk.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return string.Empty;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic sc = shell.CreateShortcut(lnk);
            string target = sc.TargetPath ?? string.Empty;
            string args = sc.Arguments ?? string.Empty;
            return string.IsNullOrEmpty(args) ? target : $"{target} {args}";
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    public IReadOnlyList<StartupConflict> FindConflicts() =>
        StartupMatcher.FindConflicts(ScanEntries(), _config.Current.Tools);

    public StartupTakeover TakeOver(StartupConflict conflict)
    {
        var e = conflict.Entry;
        if (!e.CanRemove) throw new InvalidOperationException("This entry is machine-wide and needs administrator rights to remove.");
        var takeover = new StartupTakeover
        {
            ToolId = conflict.Tool.Id,
            Location = e.Location,
            Name = e.Name,
            Command = e.Command,
            TakenAt = DateTime.Now,
        };

        if (e.Location == LocationHkcu)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(e.Name, throwOnMissingValue: false);
        }
        else if (e.Location == LocationFolder)
        {
            var file = FindStartupFile(e.Name);
            if (file is not null)
            {
                var backupDir = Path.Combine(_config.DataFolder, "startup-backup");
                Directory.CreateDirectory(backupDir);
                var backup = Path.Combine(backupDir, Path.GetFileName(file));
                File.Copy(file, backup, overwrite: true);
                File.Delete(file);
                takeover.BackupFile = backup;
                takeover.Command = file;
            }
        }

        var list = _config.Current.Startup.TakenOver;
        list.RemoveAll(t => t.Location == takeover.Location && string.Equals(t.Name, takeover.Name, StringComparison.OrdinalIgnoreCase));
        list.Add(takeover);
        var tool = _config.Current.Tools.FirstOrDefault(t => t.Id == conflict.Tool.Id);
        if (tool is not null) tool.AutoStart.Enabled = true;
        _config.Touch(ConfigArea.Startup);
        _logger.LogInformation("Took over autostart {Name} ({Location}) for {Tool}", e.Name, e.Location, conflict.Tool.Name);
        return takeover;
    }

    public void Restore(StartupTakeover takeover)
    {
        if (takeover.Location == LocationHkcu)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            key.SetValue(takeover.Name, takeover.Command);
        }
        else if (takeover.Location == LocationFolder && File.Exists(takeover.BackupFile))
        {
            var target = string.IsNullOrEmpty(takeover.Command)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), Path.GetFileName(takeover.BackupFile))
                : takeover.Command;
            File.Copy(takeover.BackupFile, target, overwrite: true);
        }
        _config.Current.Startup.TakenOver.Remove(takeover);
        var tool = _config.Current.Tools.FirstOrDefault(t => t.Id == takeover.ToolId);
        if (tool is not null) tool.AutoStart.Enabled = false;
        _config.Touch(ConfigArea.Startup);
        _logger.LogInformation("Restored autostart {Name}", takeover.Name);
    }

    public int EnforceTakeovers()
    {
        int removed = 0;
        foreach (var t in _config.Current.Startup.TakenOver.ToList())
        {
            try
            {
                if (t.Location == LocationHkcu)
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                    if (key?.GetValue(t.Name) is string cmd)
                    {
                        key.DeleteValue(t.Name, throwOnMissingValue: false);
                        t.Command = cmd; // keep the newest command for a later restore
                        removed++;
                    }
                }
                else if (t.Location == LocationFolder)
                {
                    var file = FindStartupFile(t.Name);
                    if (file is not null) { File.Delete(file); removed++; }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                _logger.LogWarning(ex, "Could not re-apply takeover of {Name}", t.Name);
            }
        }
        if (removed > 0)
        {
            _logger.LogInformation("Re-applied {Count} autostart takeover(s)", removed);
            _config.Touch(ConfigArea.Startup);
        }
        return removed;
    }

    private static string? FindStartupFile(string name)
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if (!Directory.Exists(folder)) return null;
        return Directory.EnumerateFiles(folder)
            .FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase));
    }
}
