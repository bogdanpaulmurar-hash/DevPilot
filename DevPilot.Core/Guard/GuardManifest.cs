using System.Text.Json;
using DevPilot.Core.Presets;
using DevPilot.Models;

namespace DevPilot.Core.Guard;

/// <summary>Reads the optional <c>network</c> block of a tool's <c>devpilot.tool.json</c>.</summary>
public static class GuardManifest
{
    /// <summary>The declared allowlist, or null when the tool has no manifest or no block (never throws).</summary>
    public static ToolNetworkPolicy? Network(ToolEntry tool)
    {
        try { return ToolManifest.Find(tool) is { } path ? ToolManifest.Load(path).Network : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return null; }
    }
}
