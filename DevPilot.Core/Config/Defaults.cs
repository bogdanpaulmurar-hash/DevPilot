using DevPilot.Models;

namespace DevPilot.Core.Config;

public static class Defaults
{
    /// <summary>A fresh configuration with two starter groups and default hotkeys.</summary>
    public static AppSettings Create()
    {
        var s = CreateEmpty();
        s.Groups.Add(new ToolGroup { Title = "My tools", Order = 0 });
        s.Groups.Add(new ToolGroup { Title = "Folders", Order = 1 });
        s.Hotkeys.AddRange(DefaultHotkeys());
        Normalizer.Normalize(s);
        return s;
    }

    public static AppSettings CreateEmpty() => new() { SchemaVersion = AppSettings.CurrentSchema };

    public static IEnumerable<HotkeyConfig> DefaultHotkeys() =>
    [
        new HotkeyConfig { Id = 1, Action = "hub", Modifiers = "Ctrl+Alt", Key = "Space", IsEnabled = true },
        new HotkeyConfig { Id = 2, Action = "main", Modifiers = "Ctrl+Alt", Key = "D", IsEnabled = true },
        new HotkeyConfig { Id = 3, Action = "keepawake", Modifiers = "Ctrl+Alt", Key = "K", IsEnabled = false },
    ];
}
