using DevPilot.Core.Interfaces;
using DevPilot.Core.Typer;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

public class TyperTests
{
    private sealed class FakeSender : IKeyboardSender
    {
        public List<TypingStep> Sent { get; } = [];
        public Func<int, bool> Accept { get; set; } = _ => true;
        public Action<int>? OnSend { get; set; }
        public bool Send(TypingStep step)
        {
            if (!Accept(Sent.Count)) return false;
            Sent.Add(step);
            OnSend?.Invoke(Sent.Count);
            return true;
        }
    }

    private sealed class FakeProbe : IForegroundProbe
    {
        public long Window { get; set; } = 1;
        public long GetForegroundWindow() => Window;
    }

    private static Task NoDelay(TimeSpan _, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private static TypingOptions Fast() => new() { DelayMs = 1, CountdownSeconds = 0 };

    [Fact]
    public void Plan_maps_line_breaks_and_tabs()
    {
        var steps = TypingPlan.Create("a\r\nb\rc\nd\te").Steps().ToList();
        Assert.Equal(
            [TypingStepKind.Char, TypingStepKind.Enter, TypingStepKind.Char, TypingStepKind.Enter,
             TypingStepKind.Char, TypingStepKind.Enter, TypingStepKind.Char, TypingStepKind.Tab, TypingStepKind.Char],
            steps.Select(s => s.Kind));
    }

    [Fact]
    public void Plan_drops_control_characters_and_keeps_unicode()
    {
        var plan = TypingPlan.Create("a\u0007ș😀");
        var chars = plan.Steps().Where(s => s.Kind == TypingStepKind.Char).Select(s => s.Char).ToArray();
        Assert.Equal("aș😀", new string(chars));
        Assert.Equal(4, plan.Count); // a, ș, two surrogate halves
    }

    [Fact]
    public void Plan_truncates_without_splitting_a_surrogate_pair()
    {
        var plan = TypingPlan.Create("ab😀", maxChars: 3);
        Assert.True(plan.Truncated);
        Assert.Equal(2, plan.Count);
    }

    [Fact]
    public void Plan_handles_null_and_empty()
    {
        Assert.True(TypingPlan.Create(null).IsEmpty);
        Assert.True(TypingPlan.Create("").IsEmpty);
    }

    [Fact]
    public void Preview_is_cut_for_long_text()
    {
        Assert.Equal(new string('x', 5) + "…", TypingPlan.Create(new string('x', 10)).Preview(5));
        Assert.Equal("hi", TypingPlan.Create("hi").Preview(5));
    }

    [Fact]
    public void Options_are_clamped()
    {
        var o = new TypingOptions { DelayMs = 9999, CountdownSeconds = -4, MaxChars = 0 }.Clamped();
        Assert.Equal(TypingOptions.MaxDelayMs, o.DelayMs);
        Assert.Equal(0, o.CountdownSeconds);
        Assert.Equal(1, o.MaxChars);
    }

    [Fact]
    public void Options_come_from_settings_and_are_clamped()
    {
        var o = TypingOptions.From(new TyperSettings { DelayMs = 25, CountdownSeconds = 99, ConfirmAboveChars = 10, StopOnWindowChange = false });
        Assert.Equal(25, o.DelayMs);
        Assert.Equal(TypingOptions.MaxCountdownSeconds, o.CountdownSeconds);
        Assert.Equal(10, o.ConfirmAboveChars);
        Assert.False(o.StopOnWindowChange);
        Assert.True(new TypingOptions { LineBreakAsShiftEnter = true }.Clamped().LineBreakAsShiftEnter);
        Assert.True(TypingOptions.From(new TyperSettings()).LineBreakAsShiftEnter);
    }

    [Fact]
    public async Task Session_types_everything_in_order()
    {
        var sender = new FakeSender();
        var session = new TyperSession(sender, new FakeProbe(), NoDelay);
        var plan = TypingPlan.Create("hi\nyo");
        await session.RunAsync(plan, Fast());
        Assert.Equal(TyperState.Done, session.State);
        Assert.Equal(plan.Count, sender.Sent.Count);
        Assert.Equal(plan.Steps(), sender.Sent);
    }

    [Fact]
    public async Task Line_breaks_become_shift_enter_when_requested()
    {
        var sender = new FakeSender();
        var session = new TyperSession(sender, new FakeProbe(), NoDelay);
        var o = Fast();
        o.LineBreakAsShiftEnter = true;
        await session.RunAsync(TypingPlan.Create("a\r\nb"), o);
        Assert.Equal([TypingStepKind.Char, TypingStepKind.ShiftEnter, TypingStepKind.Char], sender.Sent.Select(s => s.Kind));
    }

    [Fact]
    public async Task Session_counts_down_before_typing()
    {
        var delays = new List<TimeSpan>();
        var session = new TyperSession(new FakeSender(), new FakeProbe(), (d, ct) => { delays.Add(d); return Task.CompletedTask; });
        await session.RunAsync(TypingPlan.Create("a"), new TypingOptions { DelayMs = 0, CountdownSeconds = 3 });
        Assert.Equal(3, delays.Count(d => d == TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Stop_during_typing_halts_before_next_key()
    {
        var sender = new FakeSender();
        var session = new TyperSession(sender, new FakeProbe(), NoDelay);
        sender.OnSend = n => { if (n == 3) session.Stop(); };
        await session.RunAsync(TypingPlan.Create("abcdefgh"), Fast());
        Assert.Equal(TyperState.Stopped, session.State);
        Assert.Equal(TyperStopReason.User, session.Reason);
        Assert.Equal(3, sender.Sent.Count);
    }

    [Fact]
    public async Task Stop_during_countdown_types_nothing()
    {
        var sender = new FakeSender();
        TyperSession? session = null;
        session = new TyperSession(sender, new FakeProbe(), (d, ct) => { session!.Stop(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; });
        await session.RunAsync(TypingPlan.Create("abc"), new TypingOptions { CountdownSeconds = 2 });
        Assert.Equal(TyperState.Stopped, session.State);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Window_change_stops_typing()
    {
        var sender = new FakeSender();
        var probe = new FakeProbe();
        var session = new TyperSession(sender, probe, NoDelay);
        sender.OnSend = n => { if (n == 2) probe.Window = 99; };
        await session.RunAsync(TypingPlan.Create("abcdef"), Fast());
        Assert.Equal(TyperState.Stopped, session.State);
        Assert.Equal(TyperStopReason.WindowChanged, session.Reason);
        Assert.Equal(2, sender.Sent.Count);
    }

    [Fact]
    public async Task Window_change_is_ignored_when_disabled()
    {
        var sender = new FakeSender();
        var probe = new FakeProbe();
        var session = new TyperSession(sender, probe, NoDelay);
        sender.OnSend = n => { if (n == 2) probe.Window = 99; };
        var o = Fast();
        o.StopOnWindowChange = false;
        await session.RunAsync(TypingPlan.Create("abcdef"), o);
        Assert.Equal(TyperState.Done, session.State);
    }

    [Fact]
    public async Task Rejected_input_fails_the_session()
    {
        var sender = new FakeSender { Accept = n => n < 1 };
        var session = new TyperSession(sender, new FakeProbe(), NoDelay);
        await session.RunAsync(TypingPlan.Create("abc"), Fast());
        Assert.Equal(TyperState.Failed, session.State);
        Assert.Equal(TyperStopReason.SendFailed, session.Reason);
    }

    [Fact]
    public async Task Long_text_at_full_speed_completes()
    {
        var sender = new FakeSender();
        var session = new TyperSession(sender, new FakeProbe(), NoDelay);
        await session.RunAsync(TypingPlan.Create(new string('a', 100_000)), new TypingOptions { DelayMs = 0, CountdownSeconds = 0 });
        Assert.Equal(TyperState.Done, session.State);
        Assert.Equal(100_000, sender.Sent.Count);
    }
}
