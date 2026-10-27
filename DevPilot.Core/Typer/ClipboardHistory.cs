namespace DevPilot.Core.Typer;

/// <summary>One text that was on the clipboard.</summary>
/// <param name="Label">Shown instead of the text in lists (used for images, whose text is the file path).</param>
public sealed record ClipboardEntry(string Text, DateTime CapturedAt, bool Truncated, string? Label = null)
{
    public int Length => Text.Length;

    /// <summary>First non-empty line, whitespace collapsed, cut for a list row.</summary>
    public string Summary(int maxChars = 60)
    {
        if (Label is not null) return Label;
        var line = Text.Split('\r', '\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? string.Empty;
        line = string.Join(' ', line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= maxChars ? line : line[..maxChars] + "…";
    }
}

/// <summary>
/// The last few clipboard texts, newest first. Memory only: nothing is written to disk.
/// A text that is copied again moves to the top instead of being listed twice.
/// </summary>
public sealed class ClipboardHistory
{
    public const int DefaultCapacity = 10;

    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly int _maxChars;
    private List<ClipboardEntry> _items = [];

    public event Action? Changed;

    public ClipboardHistory(int capacity = DefaultCapacity, int maxChars = 1_000_000)
    {
        _capacity = Math.Max(1, capacity);
        _maxChars = Math.Max(1, maxChars);
    }

    public IReadOnlyList<ClipboardEntry> Items
    {
        get { lock (_gate) return _items.ToList(); }
    }

    /// <summary>Returns false for null or whitespace-only text, which is not worth a slot.</summary>
    public bool Add(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var truncated = text.Length > _maxChars;
        if (truncated)
        {
            text = text[.._maxChars];
            if (char.IsHighSurrogate(text[^1])) text = text[..^1];
        }
        lock (_gate)
        {
            _items.RemoveAll(e => e.Text == text);
            _items.Insert(0, new ClipboardEntry(text, DateTime.Now, truncated));
            if (_items.Count > _capacity) _items.RemoveRange(_capacity, _items.Count - _capacity);
        }
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (_items.Count == 0) return;
            _items = [];
        }
        Changed?.Invoke();
    }
}
