using DevPilot.Core.Guard;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

public class DenyTermTests
{
    private static LocalPolicy With(params string[] terms) => new() { DenyTerms = [.. terms] };

    [Fact]
    public void Plain_term_matches_as_case_insensitive_substring()
    {
        var p = With("acme");
        Assert.True(p.Contains("see AcmeCorp here"));
        Assert.False(p.Contains("nothing"));
    }

    [Fact]
    public void Inner_wildcard_spans_a_few_characters()
    {
        var p = With("proiect*alfa");
        Assert.True(p.Contains("proiect-x-alfa"));
        Assert.True(p.Contains("Proiect alfa"));
        Assert.False(p.Contains("proiect " + new string('x', 60) + " alfa"));
    }

    [Fact]
    public void Leading_and_trailing_stars_are_ignored()
    {
        var p = With("*.acme.local");
        Assert.True(p.Contains("host.acme.local"));
        Assert.Equal([4], p.FindDenyMatches("host.acme.local").ToList());
    }

    [Fact]
    public void Regex_term_and_invalid_entries()
    {
        var p = With(@"re:PRJ-\d{4}", "re:(broken", "***");
        Assert.True(p.Contains("see prj-1234"));
        Assert.False(p.Contains("PRJ-12"));
        Assert.Equal(2, p.InvalidTermCount);
    }

    [Fact]
    public void Preflight_reports_wildcard_hits_without_the_term()
    {
        var r = TextPreflight.Check("a\nuse proiect-x-alfa now", With("proiect*alfa"));
        var hit = Assert.Single(r.Hits);
        Assert.Equal(2, hit.Line);
        Assert.Equal(5, hit.Column);
    }
}

public class IncidentReportTests
{
    private static GuardIncident Sample(RuntimeAction action) => new(
        new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc), "My Tool", "abc", @"C:\tools\my.py", EgressMode.Block, action,
        ConnectionVerdict.NotAllowed, "example.net", "203.0.113.5", 443, [10, 11],
        new ToolNetworkPolicy { AllowedHosts = ["api.example.com"], Ports = [443] },
        [new GuardEvent(new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc), "My Tool", "example.net", "203.0.113.5", 443,
            ConnectionVerdict.NotAllowed, action),
         new GuardEvent(DateTime.UtcNow, "Other", "x.example", "198.51.100.1", 80, ConnectionVerdict.NotAllowed, action)]);

    [Fact]
    public void Report_names_what_where_and_what_was_done()
    {
        var text = IncidentReport.Format(Sample(RuntimeAction.Stop));
        Assert.Contains("example.net:443", text);
        Assert.Contains("api.example.com", text);
        Assert.Contains("stopped and blocked", text);
        Assert.Contains("12:00:00  example.net:443", text);
        Assert.DoesNotContain("x.example", text);
    }

    [Fact]
    public void Monitor_mode_report_says_the_tool_keeps_running()
    {
        Assert.Contains("keeps running", IncidentReport.Format(Sample(RuntimeAction.Notify)));
    }

    [Fact]
    public void File_name_is_safe()
    {
        Assert.Equal("incident-20261029-120000-MyTool.txt", IncidentReport.FileName(Sample(RuntimeAction.Stop)));
    }
}
