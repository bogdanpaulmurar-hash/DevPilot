using System.Text.Json;
using DevPilot.Core.Guard;
using Xunit;

namespace DevPilot.Tests;

public class GuardTextsTests
{
    [Fact]
    public void Hosts_are_split_trimmed_and_deduplicated()
    {
        var hosts = AllowlistText.ParseHosts("api.example.internal\r\n *.cdn.example.internal, API.example.internal;;  \n");
        Assert.Equal(2, hosts.Count);
        Assert.Contains("api.example.internal", hosts);
        Assert.Contains("*.cdn.example.internal", hosts);
        Assert.Empty(AllowlistText.ParseHosts(null));
    }

    [Fact]
    public void Ports_keep_only_valid_numbers()
    {
        var ports = AllowlistText.ParsePorts("443, 8080 abc 0 70000 443");
        Assert.Equal(2, ports.Count);
        Assert.Contains(443, ports);
        Assert.Contains(8080, ports);
    }

    [Fact]
    public void Text_round_trip_and_build()
    {
        var policy = AllowlistText.Build("a.example.internal\nb.example.internal", "443", false);
        Assert.Equal(2, policy.AllowedHosts.Count);
        Assert.Equal("443", AllowlistText.PortsToText(policy.Ports));
        Assert.Contains("b.example.internal", AllowlistText.HostsToText(policy.AllowedHosts));
        Assert.True(AllowlistText.Build("", "", true).AllowNone);
    }

    [Fact]
    public void Report_json_has_counts_and_rule_ids_but_no_messages()
    {
        var report = new GuardReport { ToolName = "Tool" };
        report.Findings.Add(new GuardFinding(GuardSeverity.High, "secret", "a.py", 3, "Possible embedded secret", "why", "fix"));
        report.Findings.Add(new GuardFinding(GuardSeverity.High, "deny-term", "b.py", 9, "Contains a term from the local deny-list"));
        report.Findings.Add(new GuardFinding(GuardSeverity.Info, "hardcoded-url", "a.py", 5, "Hard-coded web address"));
        var json = GuardReportExporter.ToJson([report], new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc));

        using var doc = JsonDocument.Parse(json);
        var tool = doc.RootElement.GetProperty("tools")[0];
        Assert.Equal("Tool", tool.GetProperty("name").GetString());
        Assert.Equal(2, tool.GetProperty("counts").GetProperty("high").GetInt32());
        Assert.Equal(1, tool.GetProperty("counts").GetProperty("info").GetInt32());
        Assert.Equal(1, tool.GetProperty("rules").GetProperty("secret").GetInt32());
        Assert.Equal(3, tool.GetProperty("findings").GetArrayLength());
        Assert.DoesNotContain("embedded secret", json);
        Assert.DoesNotContain("deny-list", json);
    }
}
