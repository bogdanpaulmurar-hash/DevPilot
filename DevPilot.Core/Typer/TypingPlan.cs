namespace DevPilot.Core.Typer;

/// <summary>
/// Turns clipboard text into keystrokes. Line breaks (CRLF, CR, LF) become Enter, tabs become Tab,
/// other control characters are dropped, everything else is typed as Unicode.
/// </summary>
public sealed class TypingPlan
{
    private readonly string _text;

    public int Count { get; }
    public bool Truncated { get; }
    public bool IsEmpty => Count == 0;

    private TypingPlan(string text, int count, bool truncated)
    {
        _text = text;
        Count = count;
        Truncated = truncated;
    }

    public static TypingPlan Create(string? text, int maxChars = 1_000_000)
    {
        text ??= string.Empty;
        var truncated = false;
        if (text.Length > maxChars)
        {
            text = text[..maxChars];
            // Do not leave half a surrogate pair at the cut.
            if (char.IsHighSurrogate(text[^1])) text = text[..^1];
            truncated = true;
        }
        var count = 0;
        foreach (var _ in Enumerate(text)) count++;
        return new TypingPlan(text, count, truncated);
    }

    public IEnumerable<TypingStep> Steps() => Enumerate(_text);

    /// <summary>Short text for the HUD preview, cut at <paramref name="maxChars"/>.</summary>
    public string Preview(int maxChars = 300) =>
        _text.Length <= maxChars ? _text : _text[..maxChars] + "…";

    private static IEnumerable<TypingStep> Enumerate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '\r':
                    if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                    yield return new TypingStep(TypingStepKind.Enter);
                    break;
                case '\n':
                    yield return new TypingStep(TypingStepKind.Enter);
                    break;
                case '\t':
                    yield return new TypingStep(TypingStepKind.Tab);
                    break;
                default:
                    if (!char.IsControl(c)) yield return new TypingStep(TypingStepKind.Char, c);
                    break;
            }
        }
    }
}
