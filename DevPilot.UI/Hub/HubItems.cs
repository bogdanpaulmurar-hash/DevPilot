using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DevPilot.Core.Config;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Presets;
using DevPilot.Models;
using DevPilot.UI.Mvvm;

namespace DevPilot.UI.Hub;

public sealed class HubToolVM : ObservableObject
{
    private ToolRunState _state;
    private string _statusText = string.Empty;
    private bool _isVisible = true;

    public ToolEntry Model { get; }
    public HubAppearance Look { get; }

    public HubToolVM(ToolEntry model, HubAppearance look, bool hasFlyout)
    {
        Model = model;
        Look = look;
        HasFlyout = hasFlyout;
    }

    public string Name => Model.Name;
    public string Description => Model.Description;
    public bool ShowDescription => Look.ShowDescriptions && !string.IsNullOrWhiteSpace(Model.Description);
    public string Glyph => FlyoutBuilder.GlyphOf(Model);
    public bool HasColor => Look.ShowToolColors && !string.IsNullOrWhiteSpace(Model.Color);
    public string Color => Model.Color;
    public bool Bold => Model.Bold;
    public bool Italic => Model.Italic;
    public bool HasFlyout { get; }
    public bool IsFolder => Model.IsFolderLike;

    public ToolRunState State
    {
        get => _state;
        set { if (Set(ref _state, value)) OnPropertyChanged(nameof(ShowStatus)); }
    }

    public bool ShowStatus => Look.ShowStatusDots && _state is not ToolRunState.Unknown;

    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
    public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }

    public string ToolTipText => string.IsNullOrWhiteSpace(Model.Description) ? Model.Name : $"{Model.Name}\n{Model.Description}";
}

public sealed class HubGroupVM : ObservableObject
{
    private readonly IConfigStore _config;
    private bool _collapsed;
    private bool _hasVisible = true;

    public ToolGroup Model { get; }
    public HubAppearance Look { get; }
    public ObservableCollection<HubToolVM> Tools { get; } = [];
    public ICommand ToggleCommand { get; }

    public HubGroupVM(ToolGroup model, HubAppearance look, IConfigStore config)
    {
        Model = model;
        Look = look;
        _config = config;
        _collapsed = model.Collapsed;
        ToggleCommand = new RelayCommand(() => IsCollapsed = !IsCollapsed);
    }

    public string Title => Look.GroupHeaderStyle == GroupHeaderStyle.SmallCaps ? Model.Title.ToUpperInvariant() : Model.Title;
    public bool HeaderVisible => Look.GroupHeaderStyle != GroupHeaderStyle.Hidden;
    public bool HeaderBold => Look.GroupHeaderStyle is GroupHeaderStyle.Bold or GroupHeaderStyle.SmallCaps;
    public bool HeaderSmall => Look.GroupHeaderStyle == GroupHeaderStyle.SmallCaps;
    public string Glyph => Model.Glyph;
    public bool HasGlyph => !string.IsNullOrWhiteSpace(Model.Glyph);
    public string Color => Model.Color;
    public bool HasColor => !string.IsNullOrWhiteSpace(Model.Color);
    public string CountText => Look.ShowGroupCounts ? Tools.Count(t => t.IsVisible).ToString() : string.Empty;

    public bool IsCollapsed
    {
        get => _collapsed;
        set
        {
            if (!Set(ref _collapsed, value)) return;
            OnPropertyChanged(nameof(ChevronGlyph));
            if (Look.RememberCollapsedGroups)
            {
                Model.Collapsed = value;
                _config.Touch(ConfigArea.Window); // persist without rebuilding the hub
            }
        }
    }

    public string ChevronGlyph => _collapsed ? Glyphs.ChevronRight : Glyphs.ChevronDown;

    public bool HasVisibleTools
    {
        get => _hasVisible;
        set => Set(ref _hasVisible, value);
    }

    public void RefreshCount() => OnPropertyChanged(nameof(CountText));
}

public sealed class SpotlightActionVM
{
    public required string Label { get; init; }
    public required string Glyph { get; init; }
    public required ICommand Command { get; init; }
}

public sealed class RecentCaptureVM
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public BitmapSource? Thumb { get; init; }
    public bool IsVideo { get; init; }
    public required ICommand OpenCommand { get; init; }
    public string ToolTip => $"{Name}\n{File.GetLastWriteTime(Path):g}";
}

/// <summary>The large ScreenPilot card at the top of the hub.</summary>
public sealed class SpotlightVM : ObservableObject
{
    public HubToolVM Tool { get; }
    public SpotlightOptions Options { get; }
    public ObservableCollection<SpotlightActionVM> Featured { get; } = [];
    public ObservableCollection<RecentCaptureVM> Recent { get; } = [];
    public required ICommand OpenCommand { get; init; }
    public required ICommand FolderCommand { get; init; }
    public string CapturesFolder { get; init; } = string.Empty;

    public SpotlightVM(HubToolVM tool, SpotlightOptions options)
    {
        Tool = tool;
        Options = options;
    }

    public bool HasRecent => Options.ShowRecentCaptures && Recent.Count > 0;
    public bool LargeButtons => Options.LargeButtons;
    public void NotifyRecent() => OnPropertyChanged(nameof(HasRecent));
}
