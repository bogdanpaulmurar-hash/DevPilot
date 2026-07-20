// ============================================================
// DevPilot — IT Automation Hub v1.0
// TrayTypeConfig.cs — Per-type tray visual configuration
// Phase: 13
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Text.Json.Serialization;

namespace DevPilot.Models;

/// <summary>
/// Emoji prefix + badge color for one AppEntryType in the tray menu.
/// Stored in AppSettings.TrayTypeConfigs keyed by AppEntryType.ToString().
/// </summary>
public class TrayTypeConfig
{
    /// <summary>Emoji / character shown before the entry name in the tray menu and Settings badge.</summary>
    [JsonPropertyName("emoji")]
    public string Emoji { get; set; } = string.Empty;

    /// <summary>Hex color for the type badge pill in Settings (e.g. "#569CD6").</summary>
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#666666";
}
