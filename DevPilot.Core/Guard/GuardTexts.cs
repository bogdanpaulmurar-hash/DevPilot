using System.Text.Json;

namespace DevPilot.Core.Guard;

/// <summary>Text forms of an allowlist for the editor: one host per line, ports separated by commas.</summary>
public static class AllowlistText
{
    private static readonly char[] Separators = ['\n', '\r', ',', ';', ' ', '\t'];

    public static List<string> ParseHosts(string? text) =>
        (text ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(h => h.Trim()).Where(h => h.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static List<int> ParsePorts(string? text) =>
        (text ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => int.TryParse(p, out var n) ? n : 0).Where(n => n is >= 1 and <= 65535).Distinct().ToList();

    public static string HostsToText(IEnumerable<string> hosts) => string.Join(Environment.NewLine, hosts);

    public static string PortsToText(IEnumerable<int> ports) => string.Join(", ", ports);

    public static ToolNetworkPolicy Build(string? hosts, string? ports, bool allowNone) =>
        new() { AllowedHosts = ParseHosts(hosts), Ports = ParsePorts(ports), AllowNone = allowNone };
}

/// <summary>
/// The exported report: rule ids, severities, files and line numbers, and counts. Messages are left out and findings
/// never hold matched text, so a deny-list term (or a secret) cannot end up in an exported report.
/// </summary>
public static class GuardReportExporter
{
    public static string ToJson(IEnumerable<GuardReport> reports, DateTime nowUtc)
    {
        var tools = reports.Select(r => new
        {
            name = r.ToolName,
            scannedUtc = r.CreatedUtc,
            counts = new
            {
                high = r.Count(GuardSeverity.High),
                warn = r.Count(GuardSeverity.Warn),
                info = r.Count(GuardSeverity.Info),
            },
            rules = r.Findings.GroupBy(f => f.RuleId).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
            findings = r.Findings.Select(f => new { severity = f.Severity.ToString(), rule = f.RuleId, file = f.File, line = f.Line }).ToList(),
        }).ToList();
        return JsonSerializer.Serialize(new { createdUtc = nowUtc, tools }, new JsonSerializerOptions { WriteIndented = true });
    }
}
