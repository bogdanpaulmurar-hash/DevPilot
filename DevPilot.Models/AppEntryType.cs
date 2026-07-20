// ============================================================
// DevPilot — IT Automation Hub
// AppEntryType.cs — Enum for all supported launcher entry types
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

namespace DevPilot.Models;

/// <summary>
/// All supported types for an App Launcher entry.
/// LauncherService uses this to determine how to launch the target.
/// </summary>
public enum AppEntryType
{
    /// <summary>.exe or any binary executable</summary>
    Executable,

    /// <summary>Opens a folder in Windows Explorer</summary>
    Folder,

    /// <summary>Opens a URL in the default browser</summary>
    Url,

    /// <summary>.ps1 PowerShell script</summary>
    PowerShell,

    /// <summary>.bat / .cmd batch file</summary>
    Batch,

    /// <summary>.py Python script (uses system python)</summary>
    Python,

    /// <summary>.vbs VBScript file</summary>
    VBScript,

    /// <summary>Runs an arbitrary shell command via cmd.exe /c</summary>
    ShellCommand,

    /// <summary>Opens a file with its default associated application</summary>
    Document,

    /// <summary>Opens a network share path (\server\share)</summary>
    NetworkShare,

    /// <summary>Runs an SSH command via ssh.exe</summary>
    Ssh,

    /// <summary>Runs a WSL command via wsl.exe</summary>
    Wsl,
}
