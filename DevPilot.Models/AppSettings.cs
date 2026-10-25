namespace DevPilot.Models;

/// <summary>Root of %APPDATA%\DevPilot\config.json (schema v2).</summary>
public class AppSettings
{
    public const int CurrentSchema = 2;

    public int SchemaVersion { get; set; } = CurrentSchema;

    public ThemeMode Theme { get; set; } = ThemeMode.Dark;
    /// <summary>The single accent colour of the UI.</summary>
    public string Accent { get; set; } = "#4F8EF7";

    public List<ToolGroup> Groups { get; set; } = [];
    public List<ToolEntry> Tools { get; set; } = [];

    public HubAppearance Hub { get; set; } = new();
    public KeepAwakeSettings KeepAwake { get; set; } = new();
    public StartupSettings Startup { get; set; } = new();
    public MonitorSettings Monitor { get; set; } = new();
    public List<HotkeyConfig> Hotkeys { get; set; } = [];
    public WindowSettings Window { get; set; } = new();
    public BackupSettings Backup { get; set; } = new();
    public ToolLogSettings ToolLog { get; set; } = new();
}

/// <summary>Per-tool log files in %LOCALAPPDATA%\DevPilot\logs\tools.</summary>
public class ToolLogSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Size of one log file before it is rotated (64–4096 KB).</summary>
    public int MaxKb { get; set; } = 512;
    /// <summary>Files kept per tool, current one included (1–10).</summary>
    public int Keep { get; set; } = 3;
}

/// <summary>Backups of the whole configuration (with chosen logos) to a folder the user picks.</summary>
public class BackupSettings
{
    public const int MaxKeep = 10;

    /// <summary>Target folder; empty = Documents\DevPilot Backups.</summary>
    public string Folder { get; set; } = string.Empty;
    public BackupMode Mode { get; set; } = BackupMode.Daily;
    /// <summary>How many regular backups stay in the folder (1–10); older ones are removed.</summary>
    public int Keep { get; set; } = MaxKeep;
    /// <summary>On restore, rewrite paths under the old user folder (C:\Users\old\…) to this PC's user folder.</summary>
    public bool AdaptUserFolder { get; set; } = true;
    public DateTime? LastBackup { get; set; }
}

public class KeepAwakeSettings
{
    public bool Enabled { get; set; }
    public KeepAwakeMode Mode { get; set; } = KeepAwakeMode.Silent;
    public bool KeepDisplayOn { get; set; } = true;
    /// <summary>Seconds between input nudges (MouseNudge / F15Key).</summary>
    public int IntervalSeconds { get; set; } = 60;
    /// <summary>Only nudge when the user has been idle at least this long.</summary>
    public int OnlyWhenIdleSeconds { get; set; } = 30;
    public bool ScheduleEnabled { get; set; }
    public string ScheduleFrom { get; set; } = "08:00";
    public string ScheduleTo { get; set; } = "18:00";
    /// <summary>Days the schedule applies to (Monday = 1 … Sunday = 7).</summary>
    public List<int> ScheduleDays { get; set; } = [1, 2, 3, 4, 5];
}

public class StartupSettings
{
    public bool StartWithWindows { get; set; }
    /// <summary>When started with Windows (or always, if <see cref="AlwaysStartHidden"/>) stay in the tray.</summary>
    public bool StartHidden { get; set; } = true;
    public bool AlwaysStartHidden { get; set; }
    /// <summary>Re-check every N minutes that managed tools did not re-create their own autostart entries. 0 = off.</summary>
    public int RecheckMinutes { get; set; } = 10;
    public List<StartupTakeover> TakenOver { get; set; } = [];
}

/// <summary>Backup of an autostart entry DevPilot removed from a managed tool.</summary>
public class StartupTakeover
{
    public string ToolId { get; set; } = string.Empty;
    /// <summary>"HKCU-Run" or "StartupFolder".</summary>
    public string Location { get; set; } = "HKCU-Run";
    public string Name { get; set; } = string.Empty;
    /// <summary>Original command (registry) or the full path of the original shortcut.</summary>
    public string Command { get; set; } = string.Empty;
    /// <summary>Backup copy of a Startup-folder shortcut, inside %APPDATA%\DevPilot\startup-backup.</summary>
    public string BackupFile { get; set; } = string.Empty;
    public DateTime TakenAt { get; set; } = DateTime.Now;
}

public class MonitorSettings
{
    public int IntervalSeconds { get; set; } = 5;
    public List<HealthTarget> ExtraTargets { get; set; } = [];
}

/// <summary>Stand-alone process or URL watched by the monitor (v1 heartbeat targets).</summary>
public class HealthTarget
{
    public string Id { get; set; } = NewId.Short();
    public string Name { get; set; } = string.Empty;
    /// <summary>Process name (e.g. notepad) or URL.</summary>
    public string Target { get; set; } = string.Empty;
    public bool IsUrl { get; set; }
    public bool Enabled { get; set; } = true;
}

public class HotkeyConfig
{
    public int Id { get; set; }
    /// <summary>"hub", "main", "keepawake", or "tool:{toolId}" / "action:{toolId}:{actionId}".</summary>
    public string Action { get; set; } = string.Empty;
    public string Modifiers { get; set; } = "Ctrl+Alt";
    public string Key { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}

public class WindowSettings
{
    public double Width { get; set; } = 1100;
    public double Height { get; set; } = 720;
    public string LastPage { get; set; } = "hub";
    /// <summary>Closing the main window keeps DevPilot running in the tray.</summary>
    public bool CloseToTray { get; set; } = true;
}
