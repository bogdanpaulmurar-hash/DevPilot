using DevPilot.Models;

namespace DevPilot.Core.Typer;

public sealed class TypingOptions
{
    /// <summary>Pause between keystrokes. 0 types as fast as the target accepts.</summary>
    public int DelayMs { get; set; } = 10;

    /// <summary>Seconds before typing starts, so the target field can be focused.</summary>
    public int CountdownSeconds { get; set; } = 3;

    /// <summary>Stop when another window takes the focus after typing started.</summary>
    public bool StopOnWindowChange { get; set; } = true;

    /// <summary>Texts longer than this need an explicit confirmation in the UI. 0 disables the prompt.</summary>
    public int ConfirmAboveChars { get; set; } = 2000;

    /// <summary>Safety ceiling; longer text is cut. Memory stays flat because steps are produced lazily.</summary>
    public int MaxChars { get; set; } = 1_000_000;

    public const int MaxDelayMs = 500;
    public const int MaxCountdownSeconds = 30;

    public static TypingOptions From(TyperSettings s) => new TypingOptions
    {
        DelayMs = s.DelayMs,
        CountdownSeconds = s.CountdownSeconds,
        StopOnWindowChange = s.StopOnWindowChange,
        ConfirmAboveChars = s.ConfirmAboveChars,
    }.Clamped();

    public TypingOptions Clamped() => new()
    {
        DelayMs = Math.Clamp(DelayMs, 0, MaxDelayMs),
        CountdownSeconds = Math.Clamp(CountdownSeconds, 0, MaxCountdownSeconds),
        StopOnWindowChange = StopOnWindowChange,
        ConfirmAboveChars = Math.Max(0, ConfirmAboveChars),
        MaxChars = Math.Max(1, MaxChars),
    };
}
