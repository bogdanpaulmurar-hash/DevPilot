// ============================================================
// DevPilot — IT Automation Hub
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Text.Json.Serialization;

namespace DevPilot.Models;

/// <summary>A user-defined external tool entry persisted in config.json.</summary>
public class CustomToolEntry
{
    /// <summary>Unique identifier (8-char hex, auto-generated).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Display name shown as the card title.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional subtitle shown below the name.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Emoji icon shown in the card header (default: ??).</summary>
    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "??";

    /// <summary>Full path to the executable launched via Process.Start.</summary>
    [JsonPropertyName("exe_path")]
    public string ExePath { get; set; } = string.Empty;
}