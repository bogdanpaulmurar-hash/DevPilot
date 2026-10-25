using System.Text;
using DevPilot.Core.Launch;
using DevPilot.Models;

namespace DevPilot.Core.Logging;

/// <summary>Text of the per-tool log lines. Pure functions, so the format is testable.</summary>
public static class ToolLogFormat
{
    public static string Stamp(DateTime time, string text) => $"{time:yyyy-MM-dd HH:mm:ss} {text}";

    /// <summary>Multi-line block written when DevPilot starts a tool: reason, command line, folder, DEVPILOT_* variables.</summary>
    public static string StartBlock(ToolEntry tool, LaunchReason reason, LaunchPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"START {tool.Name} ({reason})");
        sb.AppendLine($"  command : {plan.CommandLine}");
        sb.AppendLine($"  mode    : {plan.Mode}{(plan.RunAsAdmin ? ", as administrator" : string.Empty)}{(plan.HideWindow ? ", hidden window" : string.Empty)}");
        if (!string.IsNullOrWhiteSpace(plan.WorkingDirectory)) sb.AppendLine($"  folder  : {plan.WorkingDirectory}");
        foreach (var (k, v) in plan.Environment.Where(e => e.Key.StartsWith("DEVPILOT_", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.Key))
            sb.AppendLine($"  env     : {k}={v}");
        return sb.ToString().TrimEnd();
    }

    public static string StartResult(bool success, int pid, string? error) =>
        success ? $"STARTED pid {pid}" : $"FAILED {error}";

    public static string ExitLine(int exitCode, TimeSpan ranFor) =>
        $"EXIT code {exitCode} after {Duration(ranFor)}{(exitCode == 0 ? string.Empty : " (non-zero: the tool reported an error)")}";

    public static string StoppedLine(TimeSpan? ranFor) =>
        ranFor is { } r ? $"STOPPED (no longer running) after {Duration(r)}" : "STOPPED (no longer running)";

    public static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s"
        : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}m {t.Seconds}s"
        : $"{t.TotalSeconds:0.0}s";
}
