using System.Text;
using DevPilot.Models;

namespace DevPilot.Core.Guard;

/// <summary>Everything known about one connection that left a tool's allowlist.</summary>
public sealed record GuardIncident(
    DateTime TimeUtc,
    string ToolName,
    string ToolId,
    string ToolPath,
    EgressMode Mode,
    RuntimeAction Action,
    ConnectionVerdict Verdict,
    string Host,
    string Address,
    int Port,
    IReadOnlyList<int> ProcessIds,
    ToolNetworkPolicy? Policy,
    IReadOnlyList<GuardEvent> Recent);

/// <summary>
/// Plain-text incident report. It holds the facts needed to diagnose and fix the tool (what connected where, which rule
/// it broke, what DevPilot did, what to change). It never holds file contents or deny-list terms.
/// </summary>
public static class IncidentReport
{
    public const int MaxRecent = 20;

    public static string FileName(GuardIncident i)
    {
        var safe = new string(i.ToolName.Where(char.IsLetterOrDigit).ToArray());
        return $"incident-{i.TimeUtc:yyyyMMdd-HHmmss}-{(safe.Length == 0 ? "tool" : safe)}.txt";
    }

    public static string Why(ConnectionVerdict verdict, ToolNetworkPolicy? policy) => verdict switch
    {
        ConnectionVerdict.NotAllowed when policy?.AllowNone == true => "The tool is approved with no external access, but it connected outside.",
        ConnectionVerdict.NotAllowed => "The destination (or port) is not in the tool's allowlist.",
        ConnectionVerdict.NoPolicy => "The tool has no allowlist, so every external connection is only logged.",
        _ => "Allowed.",
    };

    public static string Format(GuardIncident i)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DevPilot Egress Guard - incident report");
        sb.AppendLine("Local to this PC. Do not paste into any personal or public AI tool, issue or repository.");
        sb.AppendLine();
        sb.AppendLine($"Time (UTC):  {i.TimeUtc:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Tool:        {i.ToolName} (id {i.ToolId})");
        sb.AppendLine($"Start path:  {i.ToolPath}");
        sb.AppendLine($"Processes:   {(i.ProcessIds.Count == 0 ? "-" : string.Join(", ", i.ProcessIds))}");
        sb.AppendLine($"Guard mode:  {i.Mode}");
        sb.AppendLine();
        sb.AppendLine("What happened");
        sb.AppendLine($"  Connection to {i.Host}:{i.Port} (address {i.Address})");
        sb.AppendLine($"  Verdict: {i.Verdict}. {Why(i.Verdict, i.Policy)}");
        sb.AppendLine($"  Action:  {Describe(i.Action)}");
        sb.AppendLine();
        sb.AppendLine("Allowlist in force");
        if (i.Policy is null || !i.Policy.IsDeclared) sb.AppendLine("  (none declared)");
        else if (i.Policy.AllowNone) sb.AppendLine("  no external access");
        else
        {
            sb.AppendLine($"  hosts: {(i.Policy.AllowedHosts.Count == 0 ? "-" : string.Join(", ", i.Policy.AllowedHosts))}");
            sb.AppendLine($"  ports: {(i.Policy.Ports.Count == 0 ? "any" : string.Join(", ", i.Policy.Ports))}");
        }
        sb.AppendLine();
        sb.AppendLine("Recent guard events for this tool (newest last)");
        var recent = i.Recent.Where(e => e.ToolName == i.ToolName).TakeLast(MaxRecent).ToList();
        if (recent.Count == 0) sb.AppendLine("  (none)");
        foreach (var e in recent)
            sb.AppendLine($"  {e.TimeUtc:HH:mm:ss}  {e.Host}:{e.Port}  {e.Verdict}  {e.Action}");
        sb.AppendLine();
        sb.AppendLine("What to do");
        sb.AppendLine("  1. Find out why the tool contacts this destination (update check, telemetry, API call, a library).");
        sb.AppendLine("  2. If it is legitimate and allowed where you work: add the host to the tool's network block");
        sb.AppendLine("     (devpilot.tool.json) or approve it again on the Egress Guard page, then reset the block.");
        sb.AppendLine("  3. If not: remove or disable that code in the tool. Do not widen the allowlist to make the alarm go away.");
        return sb.ToString();
    }

    private static string Describe(RuntimeAction action) => action switch
    {
        RuntimeAction.Stop => "tool stopped and blocked from starting until you review it on the Egress Guard page",
        RuntimeAction.Notify => "notification only (monitor mode); the tool keeps running",
        _ => "logged only",
    };
}
