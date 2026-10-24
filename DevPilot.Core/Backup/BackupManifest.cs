namespace DevPilot.Core.Backup;

/// <summary>
/// manifest.json inside a DevPilot backup (.zip). The archive also holds config.json, the logos the user
/// picked (logos/…) and, for manual recovery, the autostart entries DevPilot took over (startup-backup/…).
/// </summary>
public sealed class BackupManifest
{
    public const int CurrentFormat = 1;
    public const string AppName = "DevPilot";
    public const string FileName = "manifest.json";
    public const string ConfigEntry = "config.json";
    public const string LogosFolder = "logos/";
    public const string StartupFolder = "startup-backup/";

    public int Format { get; set; } = CurrentFormat;
    public string App { get; set; } = AppName;
    public string AppVersion { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Machine { get; set; } = string.Empty;
    /// <summary>The Windows user folder (C:\Users\name) of the PC that made the backup; used to adapt paths on restore.</summary>
    public string UserFolder { get; set; } = string.Empty;
    public bool Automatic { get; set; }
    /// <summary>Made by DevPilot right before a restore.</summary>
    public bool BeforeRestore { get; set; }
    public int Tools { get; set; }
    public int Groups { get; set; }
    public List<BackupLogo> Logos { get; set; } = [];
}

/// <summary>A logo file copied into the backup.</summary>
public sealed class BackupLogo
{
    /// <summary>Entry name inside the archive (logos/name.png).</summary>
    public string Entry { get; set; } = string.Empty;
    /// <summary>The tools' IconPath value exactly as it was in config.json (several tools may share one logo).</summary>
    public List<string> IconPaths { get; set; } = [];
    /// <summary>The file it was copied from (expanded), for information.</summary>
    public string Source { get; set; } = string.Empty;
}
