// ============================================================
// DevPilot — IT Automation Hub v1.0
// ITrayService.cs — Interface for system tray menu management
// Phase: 12
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

namespace DevPilot.Core.Interfaces;

/// <summary>
/// Manages the system-tray context menu for DevPilot.
/// <para>
/// Call <see cref="RebuildMenu"/> after any settings change to reflect
/// the updated ShowInTray selections without restarting the application.
/// </para>
/// </summary>
public interface ITrayService
{
    /// <summary>
    /// Reconstructs the tray context menu immediately from the current
    /// <c>AppSettings.Apps</c> where <c>ShowInTray == true</c> (max 8 entries).
    /// Thread-safe — dispatches to the UI thread if called from a background thread.
    /// </summary>
    void RebuildMenu();
}