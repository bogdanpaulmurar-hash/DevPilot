// ============================================================
// DevPilot — IT Automation Hub
// HeartbeatTarget.cs — Model for a heartbeat monitor target
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Text.Json.Serialization;

namespace DevPilot.Models;

public enum HeartbeatTargetType { Process, Url }

public class HeartbeatTarget
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public HeartbeatTargetType Type { get; set; } = HeartbeatTargetType.Process;

    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; set; } = true;
}
