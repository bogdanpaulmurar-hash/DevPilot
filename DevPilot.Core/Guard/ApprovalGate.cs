using DevPilot.Models;

namespace DevPilot.Core.Guard;

public enum GuardStatus { Unreviewed, Approved, Blocked, Drifted }

/// <summary>Whether a tool may start, and why not (or why with a warning). Messages never contain policy details.</summary>
public sealed record GateDecision(bool CanStart, GuardStatus Status, string Message, bool Checked = true)
{
    public static GateDecision Skipped { get; } = new(true, GuardStatus.Unreviewed, string.Empty, Checked: false);
}

/// <summary>
/// The approval gate: in <see cref="EgressMode.Block"/> only an Approved tool with unchanged files and remote starts;
/// in <see cref="EgressMode.Monitor"/> everything starts, with a warning when the tool is not Approved;
/// in <see cref="EgressMode.Off"/> nothing is checked. Only tools that start a process are gated (not folders, URLs…).
/// </summary>
public static class ApprovalGate
{
    public static bool Applies(ToolEntry tool) => tool.IsProcessLike;

    public static GuardStatus StatusOf(ToolEntry tool, Provenance current)
    {
        if (tool.GuardBlocked) return GuardStatus.Blocked;
        return GuardApprovals.CheckDrift(tool.GuardApproval, current).State switch
        {
            DriftState.Unchanged => GuardStatus.Approved,
            DriftState.Changed => GuardStatus.Drifted,
            _ => GuardStatus.Unreviewed,
        };
    }

    /// <summary>Decides for one start. <paramref name="current"/> is only evaluated when the gate applies.</summary>
    public static GateDecision Evaluate(EgressMode mode, ToolEntry tool, Func<Provenance> current)
    {
        if (mode == EgressMode.Off || !Applies(tool)) return GateDecision.Skipped;

        var status = StatusOf(tool, current());
        var message = status switch
        {
            GuardStatus.Approved => string.Empty,
            GuardStatus.Blocked => "Blocked in the Egress Guard.",
            GuardStatus.Drifted => "Changed since it was approved in the Egress Guard; review it again.",
            _ => "Not reviewed in the Egress Guard yet.",
        };
        bool canStart = mode == EgressMode.Monitor || status == GuardStatus.Approved;
        return new GateDecision(canStart, status, message);
    }

    /// <summary>Stores an approval for the tool as it is now. False when there is nothing to hash (folder missing).</summary>
    public static bool Approve(ToolEntry tool, Provenance current, EgressMode mode, ToolNetworkPolicy? network, DateTime nowUtc)
    {
        if (current.ManifestDigest.Length == 0) return false;
        tool.GuardApproval = GuardApprovals.Create(current, mode, network, nowUtc);
        tool.GuardBlocked = false;
        return true;
    }

    public static void Block(ToolEntry tool) => tool.GuardBlocked = true;

    /// <summary>Removes the block and any approval, so the tool goes back to Unreviewed.</summary>
    public static void Reset(ToolEntry tool)
    {
        tool.GuardBlocked = false;
        tool.GuardApproval = null;
    }
}
