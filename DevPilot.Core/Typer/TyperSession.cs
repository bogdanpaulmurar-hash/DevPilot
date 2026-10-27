using DevPilot.Core.Interfaces;

namespace DevPilot.Core.Typer;

public enum TyperState { Idle, Countdown, Typing, Done, Stopped, Failed }

public enum TyperStopReason { None, User, WindowChanged, SendFailed }

public sealed record TyperProgress(TyperState State, int Sent, int Total, int SecondsLeft, TyperStopReason Reason);

/// <summary>
/// One typing run: countdown, then keystrokes one by one. Runs on the caller's context and never blocks it;
/// <see cref="Stop"/> takes effect before the next keystroke.
/// </summary>
public sealed class TyperSession
{
    private readonly IKeyboardSender _sender;
    private readonly IForegroundProbe _probe;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly CancellationTokenSource _cts = new();

    public TyperState State { get; private set; } = TyperState.Idle;
    public TyperStopReason Reason { get; private set; } = TyperStopReason.None;
    public int Sent { get; private set; }
    public event Action<TyperProgress>? Progress;

    public TyperSession(IKeyboardSender sender, IForegroundProbe probe, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _sender = sender;
        _probe = probe;
        _delay = delay ?? Task.Delay;
    }

    /// <summary>Emergency stop. Safe to call from any thread, at any time.</summary>
    public void Stop()
    {
        if (State is TyperState.Countdown or TyperState.Typing)
        {
            Reason = TyperStopReason.User;
            _cts.Cancel();
        }
    }

    public async Task RunAsync(TypingPlan plan, TypingOptions options)
    {
        if (State != TyperState.Idle) throw new InvalidOperationException("A session can run only once.");
        var o = options.Clamped();
        var ct = _cts.Token;
        try
        {
            State = TyperState.Countdown;
            for (var s = o.CountdownSeconds; s > 0; s--)
            {
                Report(plan, s);
                await _delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            State = TyperState.Typing;
            var target = _probe.GetForegroundWindow();
            Report(plan, 0);

            var pause = TimeSpan.FromMilliseconds(o.DelayMs);
            foreach (var step in plan.Steps())
            {
                ct.ThrowIfCancellationRequested();
                if (o.StopOnWindowChange && target != 0 && _probe.GetForegroundWindow() != target)
                {
                    Finish(TyperState.Stopped, TyperStopReason.WindowChanged, plan);
                    return;
                }
                if (!_sender.Send(step))
                {
                    Finish(TyperState.Failed, TyperStopReason.SendFailed, plan);
                    return;
                }
                Sent++;
                if (Sent % 25 == 0) Report(plan, 0);

                if (o.DelayMs > 0) await _delay(pause, ct).ConfigureAwait(false);
                else if (Sent % 64 == 0) await Task.Yield(); // keep the UI and Stop responsive at full speed
            }
            Finish(TyperState.Done, TyperStopReason.None, plan);
        }
        catch (OperationCanceledException)
        {
            Finish(TyperState.Stopped, TyperStopReason.User, plan);
        }
    }

    private void Finish(TyperState state, TyperStopReason reason, TypingPlan plan)
    {
        State = state;
        Reason = reason;
        Report(plan, 0);
    }

    private void Report(TypingPlan plan, int secondsLeft) =>
        Progress?.Invoke(new TyperProgress(State, Sent, plan.Count, secondsLeft, Reason));
}
