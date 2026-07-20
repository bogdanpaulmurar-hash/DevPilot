// ============================================================
// DevPilot — IT Automation Hub v1.0
// IStartupService.cs — Contract for Windows autostart (Phase 9 / 14)
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

namespace DevPilot.Core.Interfaces;

/// <summary>
/// Reads and writes the Windows autostart registry entry for DevPilot.
/// Registry key: HKCU\Software\Microsoft\Windows\CurrentVersion\Run\DevPilot
/// </summary>
public interface IStartupService
{
    /// <summary>True if DevPilot is currently registered to start with Windows.</summary>
    bool IsStartupEnabled { get; }

    /// <summary>Enables or disables the Windows autostart registry entry.</summary>
    /// <param name="enable">True to register; false to remove.</param>
    void SetStartup(bool enable);
}