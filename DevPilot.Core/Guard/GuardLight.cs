using System.Text;

namespace DevPilot.Core.Guard;

public enum GuardLight { Unknown, Green, Amber, Red }

public sealed record GuardLightResult(GuardLight Light, string Reason);

/// <summary>One traffic light per tool: red = serious, amber = needs a look, green = approved and clean.</summary>
public static class GuardTrafficLight
{
    /// <param name="violations">Connections outside the allowlist seen for this tool since DevPilot started.</param>
    public static GuardLightResult Evaluate(GuardStatus status, GuardReport? report, int violations)
    {
        if (status == GuardStatus.Blocked) return new(GuardLight.Red, "Blocked: review it before it can start.");
        if (violations > 0) return new(GuardLight.Red, $"{violations} connection(s) outside its allowlist.");
        var high = report?.Count(GuardSeverity.High) ?? 0;
        if (high > 0) return new(GuardLight.Red, $"{high} high-risk finding(s) in the last scan.");
        if (status == GuardStatus.Drifted) return new(GuardLight.Amber, "Files changed since approval: review again.");
        if (status == GuardStatus.Unreviewed) return new(GuardLight.Amber, "Not approved yet.");
        if (report is null) return new(GuardLight.Amber, "Approved, but not scanned yet.");
        var warn = report.Count(GuardSeverity.Warn);
        if (warn > 0) return new(GuardLight.Amber, $"{warn} warning(s) in the last scan.");
        return new(GuardLight.Green, "Approved and the last scan is clean.");
    }
}

/// <summary>Findings as one block of text for a scrollable, copyable box. Capped so a big scan never floods the UI.</summary>
public static class GuardFindingsText
{
    public const int MaxShown = 60;

    public static string Format(GuardReport report, int maxShown = MaxShown)
    {
        var ordered = report.Findings.OrderByDescending(f => f.Severity).ThenBy(f => f.File).ThenBy(f => f.Line).ToList();
        var sb = new StringBuilder();
        foreach (var f in ordered.Take(maxShown))
        {
            sb.AppendLine($"[{f.Severity.ToString().ToUpperInvariant()}] {f.RuleId}  {f.File}:{f.Line}");
            sb.AppendLine($"    {f.Message}");
            var help = string.IsNullOrEmpty(f.Why) ? f.Fix : $"{f.Why} {f.Fix}";
            if (help.Length > 0) sb.AppendLine($"    {help}");
        }
        if (ordered.Count > maxShown)
            sb.AppendLine($"... and {ordered.Count - maxShown} more not shown (lowest severity last).");
        return sb.ToString().TrimEnd();
    }
}
