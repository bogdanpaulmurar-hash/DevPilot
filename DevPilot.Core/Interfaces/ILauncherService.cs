// ============================================================
// DevPilot — IT Automation Hub
// ILauncherService.cs — Launcher service interface
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using DevPilot.Models;

namespace DevPilot.Core.Interfaces;

/// <summary>
/// Launches AppEntry items using the appropriate mechanism
/// based on their AppEntryType.
/// </summary>
public interface ILauncherService
{
    /// <summary>
    /// Launches the given AppEntry. Returns true on success.
    /// Throws LauncherException on failure (caller should handle).
    /// </summary>
    Task<bool> LaunchAsync(AppEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Returns a human-readable label for the given type (for UI display).
    /// </summary>
    string GetTypeLabel(AppEntryType type);

    /// <summary>
    /// Returns all supported AppEntryType values (for ComboBox binding).
    /// </summary>
    IReadOnlyList<AppEntryType> GetSupportedTypes();
}
