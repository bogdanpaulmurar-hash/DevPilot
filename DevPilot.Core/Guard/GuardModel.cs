namespace DevPilot.Core.Guard;

/// <summary>How the Egress Guard reacts: nothing, log and warn, or refuse/stop.</summary>
public enum EgressMode { Off, Monitor, Block }

public enum GuardSeverity { Info, Warn, High }

/// <summary>One thing a scan or the runtime monitor found. Never carries a matched deny-list term.</summary>
public sealed record GuardFinding(
    GuardSeverity Severity,
    string RuleId,
    string File,
    int Line,
    string Message,
    string Why = "",
    string Fix = "");

/// <summary>The result of looking at one tool: findings, newest scan first when listed by callers.</summary>
public sealed class GuardReport
{
    public string ToolName { get; init; } = string.Empty;
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public List<GuardFinding> Findings { get; init; } = [];

    public int Count(GuardSeverity severity) => Findings.Count(f => f.Severity == severity);

    /// <summary>The worst severity present, or null when there are no findings.</summary>
    public GuardSeverity? Worst => Findings.Count == 0 ? null : Findings.Max(f => f.Severity);
}
