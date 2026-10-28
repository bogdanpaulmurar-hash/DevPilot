using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DevPilot.Core.Config;
using DevPilot.Core.Hub;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Presets;
using DevPilot.Models;
using DevPilot.UI.Mvvm;
using DevPilot.UI.Services;

namespace DevPilot.UI.Hub;

/// <summary>Theme colours the row colour rules need (taken from the active theme when the hub is built).</summary>
public sealed record RowTheme(string Surface)
{
    public static RowTheme Current()
    {
        var brush = System.Windows.Application.Current?.TryFindResource("SurfaceBrush") as System.Windows.Media.SolidColorBrush;
        var c = brush?.Color ?? System.Windows.Media.Color.FromRgb(0x1F, 0x21, 0x25);
        return new RowTheme($"#{c.R:X2}{c.G:X2}{c.B:X2}");
    }
}

public sealed class HubToolVM : ObservableObject
{
    private ToolRunState _state;
    private string _statusText = string.Empty;
    private bool _isVisible = true;
    private BitmapSource? _logo;

    public ToolEntry Model { get; }
    public HubAppearance Look { get; }

    public RowTheme Theme { get; }

    public HubToolVM(ToolEntry model, HubAppearance look, bool hasFlyout, RowTheme? theme = null)
    {
        Model = model;
        Look = look;
        HasFlyout = hasFlyout && !model.IsSeparator;
        Theme = theme ?? RowTheme.Current();
        if (!model.IsSeparator && HubLabel.WantsLogo(LabelMode))
        {
            if (LogoCache.Shared.TryGetCached(model, out var cached)) _logo = cached;
            else _ = LoadLogoAsync();
        }
    }

    // ── Label: logo, symbol and/or name ──
    /// <summary>The mode in force for this row (its own, or the hub-wide one).</summary>
    public HubLabelMode LabelMode => HubLabel.Effective(Model.LabelMode, Look.LabelMode);
    private LabelParts Parts => HubLabel.Resolve(LabelMode, _logo is not null);
    public BitmapSource? Logo => _logo;
    public bool ShowLogo => Parts.Logo;
    public bool ShowGlyph => Parts.Symbol;
    public bool ShowName => Parts.Name;
    /// <summary>The logo/symbol cell is left out entirely for "name only" rows.</summary>
    public bool ShowLead => Parts.Logo || Parts.Symbol;
    /// <summary>Logo size follows the hub font size (13 → 18 px).</summary>
    public double LogoSize => Math.Round(Look.FontSize + 5);
    public double LeadWidth => Math.Max(26, LogoSize + 8);

    private async Task LoadLogoAsync()
    {
        var logo = await LogoCache.Shared.GetAsync(Model);
        if (logo is null) return;
        _logo = logo;
        OnPropertyChanged(nameof(Logo));
        OnPropertyChanged(nameof(ShowLogo));
        OnPropertyChanged(nameof(ShowGlyph));
        OnPropertyChanged(nameof(ShowName));
        OnPropertyChanged(nameof(ShowLead));
    }

    public string Name => Model.Name;
    public string Description => Model.Description;
    public bool ShowDescription => Look.ShowDescriptions && !string.IsNullOrWhiteSpace(Model.Description);
    public string Glyph => FlyoutBuilder.GlyphOf(Model);
    /// <summary>Thin bar on the left.</summary>
    public bool HasColor => Look.ShowToolColors && RowColors.ShowsMarker(Model.ColorStyle, Model.Color);
    public string Color => Model.Color;
    /// <summary>Soft tint over the whole row ("#AARRGGBB"), empty when not highlighted.</summary>
    public string HighlightColor => Look.ShowToolColors && RowColors.ShowsHighlight(Model.ColorStyle, Model.Color)
        ? RowColors.HighlightHex(Model.Color, Look.HighlightStrength) : string.Empty;
    public bool HasHighlight => HighlightColor.Length > 0;
    /// <summary>Custom name colour when it stays readable on the row; empty = theme colour.</summary>
    public string TextColor => Look.ShowToolColors ? Readable(Model.TextColor) : string.Empty;
    public bool HasTextColor => TextColor.Length > 0;
    /// <summary>Custom symbol colour when it stays readable on the row; empty = theme colour.</summary>
    public string GlyphColor => Look.ShowToolColors ? Readable(Model.GlyphColor) : string.Empty;
    public bool HasGlyphColor => GlyphColor.Length > 0;

    private string Readable(string wanted) =>
        RowColors.Readable(wanted, Theme.Surface, HasHighlight ? Model.Color : null, Look.HighlightStrength);

    // ── Separator rows ──
    public bool IsSeparator => Model.IsSeparator;
    public string SeparatorLabel => Model.Name;
    public bool ShowSeparatorLabel => Model.SeparatorStyle == SeparatorStyle.Label && !string.IsNullOrWhiteSpace(Model.Name);
    public bool ShowSeparatorLine => Model.SeparatorStyle != SeparatorStyle.Space;
    public double SeparatorHeight => Model.SeparatorStyle == SeparatorStyle.Space ? 14 : 9;
    /// <summary>Line colour; empty = theme border colour.</summary>
    public string SeparatorColor => Look.ShowToolColors && RowColors.TryParse(Model.Color, out _) ? Model.Color : string.Empty;
    public bool HasSeparatorColor => SeparatorColor.Length > 0;
    /// <summary>Caption colour: its own text colour, else the line colour, if readable.</summary>
    public string SeparatorTextColor => !Look.ShowToolColors ? string.Empty
        : Readable(Model.TextColor) is { Length: > 0 } t ? t : Readable(Model.Color);
    public bool HasSeparatorTextColor => SeparatorTextColor.Length > 0;
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

    public RowTheme Theme { get; }

    public HubGroupVM(ToolGroup model, HubAppearance look, IConfigStore config, RowTheme? theme = null)
    {
        Model = model;
        Look = look;
        _config = config;
        Theme = theme ?? RowTheme.Current();
        _collapsed = model.Collapsed;
        ToggleCommand = new RelayCommand(() => IsCollapsed = !IsCollapsed);
        _logoCarrier = GroupHeaderLook.LogoCarrier(model);
        if (_logoCarrier is not null)
        {
            if (LogoCache.Shared.TryGetCached(_logoCarrier, out var cached)) _logo = cached;
            else _ = LoadLogoAsync();
        }
    }

    // ── Logo, alignment and title font (per group) ──
    private readonly ToolEntry? _logoCarrier;
    private BitmapSource? _logo;
    public BitmapSource? Logo => _logo;
    public bool HasLogo => _logo is not null;
    /// <summary>The symbol is shown only when there is no logo, so the header never carries both.</summary>
    public bool ShowGlyph => HasGlyph && !HasLogo;
    public double LogoSize => GroupHeaderLook.LogoSize(TitleFontSize);

    private async Task LoadLogoAsync()
    {
        var logo = await LogoCache.Shared.GetAsync(_logoCarrier!);
        if (logo is null) return;
        _logo = logo;
        OnPropertyChanged(nameof(Logo));
        OnPropertyChanged(nameof(HasLogo));
        OnPropertyChanged(nameof(ShowGlyph));
    }

    public bool IsCentered => Model.TitleAlign == GroupTitleAlign.Center;
    public System.Windows.HorizontalAlignment TitleAlignment =>
        IsCentered ? System.Windows.HorizontalAlignment.Center : System.Windows.HorizontalAlignment.Left;
    public double TitleFontSize => GroupHeaderLook.TitleSize(Model.TitleSize, Look.GroupHeaderStyle, Look.FontSize);
    public System.Windows.FontWeight TitleWeight =>
        System.Windows.FontWeight.FromOpenTypeWeight(GroupHeaderLook.TitleWeight(Model, Look.GroupHeaderStyle));
    public System.Windows.FontStyle TitleStyle => Model.Italic ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal;
    public bool HasTitleFont => GroupHeaderLook.FontFamily(Model.TitleFont).Length > 0;
    public System.Windows.Media.FontFamily? TitleFontFamily =>
        HasTitleFont ? new System.Windows.Media.FontFamily(GroupHeaderLook.FontFamily(Model.TitleFont)) : null;

    public string Title => Look.GroupHeaderStyle == GroupHeaderStyle.SmallCaps ? Model.Title.ToUpperInvariant() : Model.Title;
    public bool HeaderVisible => Look.GroupHeaderStyle != GroupHeaderStyle.Hidden;
    public bool HeaderBold => Look.GroupHeaderStyle is GroupHeaderStyle.Bold or GroupHeaderStyle.SmallCaps;
    public bool HeaderSmall => Look.GroupHeaderStyle == GroupHeaderStyle.SmallCaps;
    public string Glyph => Model.Glyph;
    public bool HasGlyph => !string.IsNullOrWhiteSpace(Model.Glyph);
    public string Color => Model.Color;
    /// <summary>Colour dot before the title.</summary>
    public bool HasColor => RowColors.ShowsMarker(Model.ColorStyle, Model.Color);
    /// <summary>Header tint ("#AARRGGBB"), empty when not highlighted.</summary>
    public string HighlightColor => RowColors.ShowsHighlight(Model.ColorStyle, Model.Color)
        ? RowColors.HighlightHex(Model.Color, Look.HighlightStrength) : string.Empty;
    public bool HasHighlight => HighlightColor.Length > 0;
    public string TextColor => Readable(Model.TextColor);
    public bool HasTextColor => TextColor.Length > 0;
    public string GlyphColor => Readable(Model.GlyphColor);
    public bool HasGlyphColor => GlyphColor.Length > 0;

    private string Readable(string wanted) =>
        RowColors.Readable(wanted, Theme.Surface, HasHighlight ? Model.Color : null, Look.HighlightStrength);

    public string CountText => Look.ShowGroupCounts ? Tools.Count(t => t.IsVisible && !t.IsSeparator).ToString() : string.Empty;

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
