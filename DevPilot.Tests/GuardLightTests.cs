using DevPilot.Core.Guard;
using Xunit;

namespace DevPilot.Tests;

public class GuardLightTests
{
    private static GuardReport Report(params GuardSeverity[] severities) => new()
    {
        Findings = severities.Select((s, i) => new GuardFinding(s, "r", $"f{i}.cs", i + 1, "m")).ToList(),
    };

    [Fact]
    public void Blocked_or_violation_or_high_finding_is_red()
    {
        Assert.Equal(GuardLight.Red, GuardTrafficLight.Evaluate(GuardStatus.Blocked, null, 0).Light);
        Assert.Equal(GuardLight.Red, GuardTrafficLight.Evaluate(GuardStatus.Approved, Report(), 2).Light);
        Assert.Equal(GuardLight.Red, GuardTrafficLight.Evaluate(GuardStatus.Approved, Report(GuardSeverity.High), 0).Light);
    }

    [Fact]
    public void Unreviewed_drifted_unscanned_or_warnings_are_amber()
    {
        Assert.Equal(GuardLight.Amber, GuardTrafficLight.Evaluate(GuardStatus.Unreviewed, Report(), 0).Light);
        Assert.Equal(GuardLight.Amber, GuardTrafficLight.Evaluate(GuardStatus.Drifted, Report(), 0).Light);
        Assert.Equal(GuardLight.Amber, GuardTrafficLight.Evaluate(GuardStatus.Approved, null, 0).Light);
        Assert.Equal(GuardLight.Amber, GuardTrafficLight.Evaluate(GuardStatus.Approved, Report(GuardSeverity.Warn), 0).Light);
    }

    [Fact]
    public void Approved_with_clean_or_info_scan_is_green()
    {
        Assert.Equal(GuardLight.Green, GuardTrafficLight.Evaluate(GuardStatus.Approved, Report(), 0).Light);
        Assert.Equal(GuardLight.Green, GuardTrafficLight.Evaluate(GuardStatus.Approved, Report(GuardSeverity.Info), 0).Light);
    }

    [Fact]
    public void Findings_text_is_capped_and_worst_first()
    {
        var report = Report(Enumerable.Repeat(GuardSeverity.Info, 100).Append(GuardSeverity.High).ToArray());
        var text = GuardFindingsText.Format(report);
        Assert.StartsWith("[HIGH]", text);
        Assert.Contains("and 41 more not shown", text);
        Assert.True(text.Split('\n').Length < 200);
    }
}
