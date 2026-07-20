// ============================================================
// DevPilot — IT Automation Hub v1.0
// AppConstants.cs — Application-wide string/numeric constants
// Phase: 9 (+hotkey actions, +jiggler methods, +startup key)
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

namespace DevPilot.UI;

/// <summary>Central registry of all magic-string constants used across DevPilot.UI.</summary>
public static class AppConstants
{
    // ── Identity ──────────────────────────────────────────────────────────
    public const string AppName        = "DevPilot";
    public const string AppVersion     = "1.0.0";
    public const string AppAuthor      = "Bogdan Murar (Z003FU5C)";
    public const string Company        = "Siemens AG";
    public const string PrimaryColor   = "#009999"; // Siemens Teal
    public const string ConfigFileName = "config.json";
    public const string ConfigFolder   = "DevPilot"; // under %APPDATA%

    // ── Startup registry ──────────────────────────────────────────────────
    /// <summary>Value name used in HKCU Run key. Must match StartupService.StartupKeyName.</summary>
    public const string StartupRegistryKeyName = "DevPilot";

    // ── Hotkey action identifiers ─────────────────────────────────────────
    public const string HotkeyActionShowHide      = "ShowHide";
    public const string HotkeyActionToggleJiggler = "ToggleJiggler";
    public const string HotkeyActionScreenshot    = "Screenshot";

    // ── Jiggler method values (stored in AppSettings.JigglerMethod) ───────
    public const string JigglerMethodMouseMove = "Mouse Move";
    public const string JigglerMethodKeyPress  = "Key Press";

    // ── Tab names (used as keys in AppSettings.TabVisibility) ────────────
    public const string TabTools      = "Tools";
    public const string TabJiggler    = "Jiggler";
    public const string TabScreenshot = "Screenshot";
    public const string TabHeartbeat  = "Heartbeat";
}
