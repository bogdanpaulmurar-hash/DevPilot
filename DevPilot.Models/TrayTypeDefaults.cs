// ============================================================
// DevPilot — IT Automation Hub v1.0
// TrayTypeDefaults.cs — Built-in emoji + color presets per AppEntryType
// Phase: 13
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

namespace DevPilot.Models;

/// <summary>
/// Factory for the default per-type tray appearance presets.
/// Called once as the property initializer in <see cref="AppSettings"/>;
/// also used as fallback in LoadFromSettings when a type key is missing.
/// </summary>
public static class TrayTypeDefaults
{
    public static Dictionary<string, TrayTypeConfig> Build() => new()
    {
        ["Folder"]       = new TrayTypeConfig { Emoji = "📁", Color = "#F0A500" },
        ["Executable"]   = new TrayTypeConfig { Emoji = "⚙",  Color = "#569CD6" },
        ["Python"]       = new TrayTypeConfig { Emoji = "🐍", Color = "#4CAF50" },
        ["PowerShell"]   = new TrayTypeConfig { Emoji = "🔵", Color = "#5A95D8" },
        ["Batch"]        = new TrayTypeConfig { Emoji = "🔧", Color = "#CE9178" },
        ["Url"]          = new TrayTypeConfig { Emoji = "🌐", Color = "#9CDCFE" },
        ["Document"]     = new TrayTypeConfig { Emoji = "📄", Color = "#DCDCAA" },
        ["NetworkShare"] = new TrayTypeConfig { Emoji = "📂", Color = "#C586C0" },
        ["Ssh"]          = new TrayTypeConfig { Emoji = "🔑", Color = "#4EC9B0" },
        ["Wsl"]          = new TrayTypeConfig { Emoji = "🐧", Color = "#89D185" },
        ["ShellCommand"] = new TrayTypeConfig { Emoji = "▶",  Color = "#D7996B" },
        ["VBScript"]     = new TrayTypeConfig { Emoji = "📜", Color = "#A8A830" },
    };
}
