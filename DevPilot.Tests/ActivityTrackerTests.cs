using DevPilot.Core.Activity;
using Xunit;

namespace DevPilot.Tests;

public sealed class ActivityTrackerTests
{
    [Fact]
    public void Idle_when_nothing_runs()
    {
        var t = new ActivityTracker();
        Assert.False(t.Current.IsActive);
    }

    [Fact]
    public void Scope_shows_label_and_ends_on_dispose()
    {
        var t = new ActivityTracker();
        var changes = 0;
        t.Changed += () => changes++;
        var s = t.Begin("Backup");
        Assert.True(t.Current.IsActive);
        Assert.Equal("Backup", t.Current.Label);
        Assert.Null(t.Current.Fraction);
        s.Dispose();
        Assert.False(t.Current.IsActive);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Report_sets_clamped_fraction_and_detail()
    {
        var t = new ActivityTracker();
        using var s = t.Begin("Pull");
        s.Report(0.5, "3 of 6");
        Assert.Equal(0.5, t.Current.Fraction);
        Assert.Equal("Pull · 3 of 6", t.Current.Label);
        s.Report(7);
        Assert.Equal(1, t.Current.Fraction);
    }

    [Fact]
    public void Several_actions_show_latest_with_count_and_no_percentage()
    {
        var t = new ActivityTracker();
        using var a = t.Begin("Backup");
        a.Report(0.2);
        using var b = t.Begin("Start tool");
        Assert.Equal("Start tool (+1)", t.Current.Label);
        Assert.Null(t.Current.Fraction);
        b.Dispose();
        Assert.Equal(0.2, t.Current.Fraction);
    }

    [Fact]
    public void Double_dispose_is_harmless()
    {
        var t = new ActivityTracker();
        var s = t.Begin("x");
        s.Dispose();
        s.Dispose();
        Assert.False(t.Current.IsActive);
    }
}
