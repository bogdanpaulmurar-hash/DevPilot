using DevPilot.Core.Config;
using DevPilot.Core.Hub;
using DevPilot.Core.Launch;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

public class RowColorsTests
{
    private const string DarkSurface = "#1F2125";
    private const string LightSurface = "#FFFFFF";

    [Theory]
    [InlineData("#E5534B", true, 0xE5, 0x53, 0x4B)]
    [InlineData("e5534b", true, 0xE5, 0x53, 0x4B)]
    [InlineData("#80E5534B", true, 0xE5, 0x53, 0x4B)]
    [InlineData("", false, 0, 0, 0)]
    [InlineData("#12345", false, 0, 0, 0)]
    [InlineData("#GGGGGG", false, 0, 0, 0)]
    public void Parses_hex(string hex, bool ok, int r, int g, int b)
    {
        Assert.Equal(ok, RowColors.TryParse(hex, out var c));
        if (ok) Assert.Equal(new Rgb((byte)r, (byte)g, (byte)b), c);
    }

    [Theory]
    [InlineData(RowColorStyle.Marker, true, false)]
    [InlineData(RowColorStyle.Highlight, false, true)]
    [InlineData(RowColorStyle.MarkerAndHighlight, true, true)]
    public void Style_decides_marker_and_tint(RowColorStyle style, bool marker, bool tint)
    {
        Assert.Equal(marker, RowColors.ShowsMarker(style, "#4F8EF7"));
        Assert.Equal(tint, RowColors.ShowsHighlight(style, "#4F8EF7"));
        // No colour, nothing to show.
        Assert.False(RowColors.ShowsMarker(style, ""));
        Assert.False(RowColors.ShowsHighlight(style, ""));
    }

    [Fact]
    public void Highlight_is_a_soft_tint_with_clamped_strength()
    {
        Assert.Equal("#2E4F8EF7", RowColors.HighlightHex("#4F8EF7", 18)); // 18% → alpha 0x2E
        Assert.Equal(RowColors.HighlightHex("#4F8EF7", RowColors.MaxStrength), RowColors.HighlightHex("#4F8EF7", 100));
        Assert.Equal(RowColors.HighlightHex("#4F8EF7", RowColors.MinStrength), RowColors.HighlightHex("#4F8EF7", 0));
        Assert.Equal(string.Empty, RowColors.HighlightHex("", 18));
    }

    [Fact]
    public void Contrast_matches_wcag_extremes()
    {
        Assert.Equal(21, RowColors.Contrast(new Rgb(0, 0, 0), new Rgb(255, 255, 255)), 1);
        Assert.Equal(1, RowColors.Contrast(new Rgb(10, 20, 30), new Rgb(10, 20, 30)), 3);
    }

    [Fact]
    public void Readable_colour_is_kept()
    {
        Assert.Equal("#E5534B", RowColors.Readable("#e5534b", DarkSurface, null, 18));
        Assert.Equal("#E5534B", RowColors.Readable("#E5534B", LightSurface, null, 18));
    }

    [Fact]
    public void Unreadable_colour_falls_back_to_theme()
    {
        // Near-black text on the dark panel, near-white text on the light panel.
        Assert.Equal(string.Empty, RowColors.Readable("#202020", DarkSurface, null, 18));
        Assert.Equal(string.Empty, RowColors.Readable("#F5F5F5", LightSurface, null, 18));
        Assert.Equal(string.Empty, RowColors.Readable("", DarkSurface, null, 18));
    }

    [Fact]
    public void Readability_accounts_for_the_row_tint()
    {
        var plain = RowColors.Background(LightSurface, null, 18);
        var tinted = RowColors.Background(LightSurface, "#1F2125", 60);
        Assert.Equal(new Rgb(255, 255, 255), plain);
        Assert.NotEqual(plain, tinted);
        // Mid grey: readable on plain white, unreadable on a strong dark tint.
        Assert.Equal("#707070", RowColors.Readable("#707070", LightSurface, null, 60));
        Assert.Equal(string.Empty, RowColors.Readable("#707070", LightSurface, "#1F2125", 60));
    }

    [Fact]
    public void Normalizer_cleans_colours_and_makes_separators_inert()
    {
        var s = Defaults.Create();
        var g = new ToolGroup { Title = "G", TextColor = "abc", GlyphColor = "ff0000" };
        s.Groups.Add(g);
        var sep = new ToolEntry
        {
            Kind = ToolKind.Separator, Name = "Work", GroupId = g.Id, TextColor = "zz", GlyphColor = "00ff00",
            Tray = TrayMode.DevPilotIcon, Spotlight = true,
        };
        sep.AutoStart.Enabled = true;
        s.Tools.Add(sep);
        s.Hub.HighlightStrength = 500;

        Normalizer.Normalize(s);

        Assert.Equal(string.Empty, g.TextColor);
        Assert.Equal("#FF0000", g.GlyphColor);
        Assert.Equal(string.Empty, sep.TextColor);
        Assert.Equal("#00FF00", sep.GlyphColor);
        Assert.True(sep.IsSeparator);
        Assert.Equal(TrayMode.HubOnly, sep.Tray);
        Assert.False(sep.AutoStart.Enabled);
        Assert.False(sep.Spotlight);
        Assert.Equal(RowColors.MaxStrength, s.Hub.HighlightStrength);
    }

    [Fact]
    public void New_fields_round_trip_and_old_configs_keep_defaults()
    {
        var s = Defaults.Create();
        var g = new ToolGroup { Title = "G", ColorStyle = RowColorStyle.Highlight, TextColor = "#E5534B" };
        s.Groups.Add(g);
        s.Tools.Add(new ToolEntry { Kind = ToolKind.Separator, SeparatorStyle = SeparatorStyle.Label, Name = "Docs", GroupId = g.Id });
        var json = ConfigSerializer.Serialize(s);
        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json, ConfigSerializer.Options)!;
        Assert.Contains(back.Groups, x => x.ColorStyle == RowColorStyle.Highlight && x.TextColor == "#E5534B");
        Assert.Contains(back.Tools, x => x.IsSeparator && x.SeparatorStyle == SeparatorStyle.Label && x.Name == "Docs");

        var old = System.Text.Json.JsonSerializer.Deserialize<ToolEntry>("""{ "name": "A", "color": "#4F8EF7" }""", ConfigSerializer.Options)!;
        Assert.Equal(RowColorStyle.Marker, old.ColorStyle);
        Assert.Equal(string.Empty, old.TextColor);
    }

    [Fact]
    public void Separator_never_launches_or_tracks()
    {
        var sep = new ToolEntry { Kind = ToolKind.Separator, Name = "Work" };
        Assert.Throws<InvalidOperationException>(() => LaunchPlanner.ForTool(sep));
        Assert.False(ProcessMatcher.CanTrack(sep));
        Assert.False(sep.IsProcessLike);
        Assert.False(HubSearch.Matches(sep, null, "zzz"));
    }
}
