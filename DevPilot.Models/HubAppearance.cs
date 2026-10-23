namespace DevPilot.Models;

/// <summary>Everything the user can tune about the tray hub panel.</summary>
public class HubAppearance
{
    public int Width { get; set; } = 380;
    public int MaxHeight { get; set; } = 680;
    public int CornerRadius { get; set; } = 10;
    /// <summary>0.70 – 1.00</summary>
    public double Opacity { get; set; } = 1.0;
    public HubDensity Density { get; set; } = HubDensity.Comfortable;
    public double FontSize { get; set; } = 13;
    public HubAnchor Anchor { get; set; } = HubAnchor.Auto;
    public FlyoutSide FlyoutSide { get; set; } = FlyoutSide.Auto;
    public int FlyoutWidth { get; set; } = 260;
    public int HoverDelayMs { get; set; } = 250;
    public bool ShowSearch { get; set; } = true;
    public bool ShowDescriptions { get; set; }
    public bool ShowStatusDots { get; set; } = true;
    public bool ShowGroupCounts { get; set; } = true;
    public bool ShowToolColors { get; set; } = true;
    /// <summary>Strength of the row tint for highlighted rows, in percent (5 – 60).</summary>
    public int HighlightStrength { get; set; } = 18;
    public GroupHeaderStyle GroupHeaderStyle { get; set; } = GroupHeaderStyle.SmallCaps;
    public bool CloseAfterLaunch { get; set; } = true;
    public bool RememberCollapsedGroups { get; set; } = true;

    public TrayClickAction LeftClick { get; set; } = TrayClickAction.OpenHub;
    public TrayClickAction RightClick { get; set; } = TrayClickAction.QuickMenu;
    public TrayClickAction DoubleClick { get; set; } = TrayClickAction.Nothing;

    public SpotlightOptions Spotlight { get; set; } = new();
}

/// <summary>The special ScreenPilot card at the top of the hub.</summary>
public class SpotlightOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>Own accent for the card (#RRGGBB); empty = global accent.</summary>
    public string Accent { get; set; } = string.Empty;
    public bool ShowRecentCaptures { get; set; } = true;
    public int RecentCount { get; set; } = 4;
    /// <summary>Captures folder; empty = %USERPROFILE%\Pictures\ScreenPilot.</summary>
    public string CapturesFolder { get; set; } = string.Empty;
    public bool LargeButtons { get; set; } = true;
}
