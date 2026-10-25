namespace DevPilot.Core.Cloud;

/// <summary>What autostart is waiting for right now, per tool (shown on the Startup page).</summary>
public sealed class AutoStartStatus
{
    private readonly object _lock = new();
    private readonly Dictionary<string, string> _items = new();

    public event EventHandler? Changed;

    public void Set(string toolName, string? text)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(text)) _items.Remove(toolName);
            else _items[toolName] = text;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<string> Lines()
    {
        lock (_lock) return _items.Select(kv => $"{kv.Key}: {kv.Value}").ToList();
    }
}
