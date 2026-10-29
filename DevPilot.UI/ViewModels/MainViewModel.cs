using System.Collections.ObjectModel;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Presets;
using DevPilot.UI.Mvvm;

namespace DevPilot.UI.ViewModels;

public sealed class NavItem
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Glyph { get; init; }
    public required object Page { get; init; }
}

public sealed class MainViewModel : ObservableObject
{
    private readonly IConfigStore _config;
    private NavItem _selected;

    public ObservableCollection<NavItem> Pages { get; }
    public ObservableCollection<NavItem> FooterPages { get; }
    public string Version => "v" + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "2.0.0");

    public MainViewModel(IConfigStore config, OverviewViewModel overview, ToolsViewModel tools, KeepAwakeViewModel keepAwake,
        StartupViewModel startup, MonitorViewModel monitor, GuardViewModel guard, AppearanceViewModel appearance, SettingsViewModel settings, GuideViewModel guide)
    {
        _config = config;
        Pages =
        [
            new NavItem { Key = "hub", Title = "Overview", Glyph = Glyphs.Home, Page = overview },
            new NavItem { Key = "tools", Title = "Tools & groups", Glyph = Glyphs.List, Page = tools },
            new NavItem { Key = "keepawake", Title = "Keep-awake", Glyph = Glyphs.Sun, Page = keepAwake },
            new NavItem { Key = "startup", Title = "Startup & conflicts", Glyph = Glyphs.Play, Page = startup },
            new NavItem { Key = "monitor", Title = "Monitor", Glyph = Glyphs.Diagnostic, Page = monitor },
            new NavItem { Key = "guard", Title = "Egress Guard", Glyph = Glyphs.Lock, Page = guard },
            new NavItem { Key = "appearance", Title = "Appearance", Glyph = Glyphs.Color, Page = appearance },
        ];
        FooterPages =
        [
            new NavItem { Key = "settings", Title = "Settings", Glyph = Glyphs.Settings, Page = settings },
            new NavItem { Key = "guide", Title = "Guide", Glyph = Glyphs.Info, Page = guide },
        ];
        _selected = Find(config.Current.Window.LastPage) ?? Pages[0];
    }

    public NavItem? Find(string? key) =>
        Pages.Concat(FooterPages).FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    public NavItem Selected
    {
        get => _selected;
        set
        {
            if (value is null || !Set(ref _selected, value)) return;
            OnPropertyChanged(nameof(SelectedFooter));
            OnPropertyChanged(nameof(SelectedMain));
            _config.Current.Window.LastPage = value.Key;
            _config.Touch(ConfigArea.Window);
            if (value.Page is IPage page) page.OnShown();
        }
    }

    /// <summary>Bound to the two sidebar lists so only one shows a selection.</summary>
    public NavItem? SelectedMain
    {
        get => Pages.Contains(_selected) ? _selected : null;
        set { if (value is not null) Selected = value; }
    }

    public NavItem? SelectedFooter
    {
        get => FooterPages.Contains(_selected) ? _selected : null;
        set { if (value is not null) Selected = value; }
    }

    public void Navigate(string? key)
    {
        if (Find(key) is { } item) Selected = item;
        else if (_selected.Page is IPage p) p.OnShown();
    }
}

/// <summary>Pages refresh data when shown.</summary>
public interface IPage
{
    void OnShown();
}
