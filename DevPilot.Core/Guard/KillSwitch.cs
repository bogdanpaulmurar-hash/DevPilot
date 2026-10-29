using DevPilot.Models;

namespace DevPilot.Core.Guard;

/// <summary>
/// The kill switch ("Pause external access"): while paused, tools that could reach the network are not started and the
/// running ones are stopped. A tool whose allowlist says it needs no external access (<c>allowNone</c>) is left alone.
/// </summary>
public static class KillSwitch
{
    public const string PausedMessage = "External access is paused in the Egress Guard.";

    /// <summary>True for tools that start a process and have not declared that they need no external access.</summary>
    public static bool IsNetworkCapable(ToolEntry tool, ToolNetworkPolicy? manifest)
    {
        if (!ApprovalGate.Applies(tool)) return false;
        return !(ApprovalGate.PolicyFor(tool, manifest)?.AllowNone ?? false);
    }

    /// <summary>A refusal while paused for a network-capable tool, otherwise null (start may go on to the normal gate).</summary>
    public static GateDecision? Check(bool paused, ToolEntry tool, ToolNetworkPolicy? manifest) =>
        paused && IsNetworkCapable(tool, manifest)
            ? new GateDecision(false, ApprovalGate.StatusOf(tool, new Provenance(string.Empty, string.Empty, string.Empty, 0, false)), PausedMessage)
            : null;
}
