using DevPilot.Core.Hub;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

public class HubPlacementTests
{
    // 1920x1080 monitor with a 40px taskbar at the bottom.
    private static readonly PixelRect Work = new(0, 0, 1920, 1040);

    [Fact]
    public void Auto_bottom_taskbar_keeps_bottom_right_corner_fixed_when_height_changes()
    {
        var a = HubPlacement.CreateAnchor(HubAnchor.Auto, fromTray: true, TaskbarSide.Bottom, 1700, 1060, Work, 380, 400);

        var small = HubPlacement.Place(a, 380, 400);
        var tall = HubPlacement.Place(a, 380, 700);

        Assert.Equal(1700 + HubPlacement.TrayOverhang - 380, small.X);
        Assert.Equal(small.X, tall.X);                 // no horizontal jump
        Assert.Equal(Work.Bottom, small.Y + 400);      // bottom edge on the taskbar
        Assert.Equal(Work.Bottom, tall.Y + 700);       // still there after expanding
    }

    [Fact]
    public void Placement_does_not_depend_on_where_the_cursor_is_later()
    {
        // The anchor is captured once; Place() has no cursor input, so clicking rows can't move the panel.
        var a = HubPlacement.CreateAnchor(HubAnchor.Auto, true, TaskbarSide.Bottom, 1700, 1060, Work, 380, 400);
        Assert.Equal(HubPlacement.Place(a, 380, 500), HubPlacement.Place(a, 380, 500));
    }

    [Fact]
    public void Auto_top_taskbar_grows_downwards()
    {
        var work = new PixelRect(0, 40, 1920, 1080);
        var a = HubPlacement.CreateAnchor(HubAnchor.Auto, true, TaskbarSide.Top, 1700, 20, work, 380, 400);
        Assert.Equal(40, HubPlacement.Place(a, 380, 400).Y);
        Assert.Equal(40, HubPlacement.Place(a, 380, 800).Y);
    }

    [Theory]
    [InlineData(TaskbarSide.Left)]
    [InlineData(TaskbarSide.Right)]
    public void Auto_side_taskbar_keeps_x_and_bottom_fixed(TaskbarSide side)
    {
        var work = side == TaskbarSide.Left ? new PixelRect(60, 0, 1920, 1080) : new PixelRect(0, 0, 1860, 1080);
        var a = HubPlacement.CreateAnchor(HubAnchor.Auto, true, side, side == TaskbarSide.Left ? 30 : 1890, 900, work, 380, 300);

        var p1 = HubPlacement.Place(a, 380, 300);
        var p2 = HubPlacement.Place(a, 380, 600);

        Assert.Equal(side == TaskbarSide.Left ? work.Left : work.Right - 380, p1.X);
        Assert.Equal(p1.X, p2.X);
        Assert.Equal(p1.Y + 300, p2.Y + 600);
    }

    [Theory]
    [InlineData(HubAnchor.BottomRight, 1920 - 380, 1040 - 500)]
    [InlineData(HubAnchor.BottomLeft, 0, 1040 - 500)]
    [InlineData(HubAnchor.TopRight, 1920 - 380, 0)]
    [InlineData(HubAnchor.TopLeft, 0, 0)]
    public void Fixed_corners(HubAnchor anchor, int x, int y)
    {
        var a = HubPlacement.CreateAnchor(anchor, true, TaskbarSide.Bottom, 0, 0, Work, 380, 300);
        Assert.Equal((x, y), HubPlacement.Place(a, 380, 500));
    }

    [Fact]
    public void Center_keeps_top_edge_after_opening()
    {
        var a = HubPlacement.CreateAnchor(HubAnchor.Center, true, TaskbarSide.Bottom, 0, 0, Work, 400, 400);
        var first = HubPlacement.Place(a, 400, 400);
        Assert.Equal(((1920 - 400) / 2, (1040 - 400) / 2), first);
        Assert.Equal(first, HubPlacement.Place(a, 400, 600));
    }

    [Fact]
    public void Auto_without_tray_click_means_center()
    {
        var a = HubPlacement.CreateAnchor(HubAnchor.Auto, fromTray: false, TaskbarSide.Bottom, 5, 5, Work, 400, 400);
        Assert.Equal(HubAnchor.Center, a.Mode);
    }

    [Fact]
    public void Panel_stays_inside_the_work_area()
    {
        // Tray icon near the left edge: the panel would start off-screen.
        var a = HubPlacement.CreateAnchor(HubAnchor.Auto, true, TaskbarSide.Bottom, 100, 1060, Work, 380, 400);
        Assert.Equal(0, HubPlacement.Place(a, 380, 400).X);

        // Taller than the screen: top edge pinned to the work area top.
        Assert.Equal(0, HubPlacement.Place(a, 380, 1500).Y);
    }

    [Fact]
    public void Repeated_tray_clicks_on_the_same_icon_reuse_the_anchor()
    {
        var first = HubPlacement.CreateAnchor(HubAnchor.Auto, true, TaskbarSide.Bottom, 1700, 1060, Work, 380, 400);
        var again = HubPlacement.CreateAnchor(HubAnchor.Auto, true, TaskbarSide.Bottom, 1712, 1052, Work, 380, 400);
        Assert.True(HubPlacement.ShouldReuse(first, again, pinned: false));

        var otherIcon = HubPlacement.CreateAnchor(HubAnchor.Auto, true, TaskbarSide.Bottom, 1500, 1060, Work, 380, 400);
        Assert.False(HubPlacement.ShouldReuse(first, otherIcon, false));

        var otherMonitor = HubPlacement.CreateAnchor(HubAnchor.Auto, true, TaskbarSide.Bottom, 1700, 1060,
            new PixelRect(1920, 0, 3840, 1040), 380, 400);
        Assert.False(HubPlacement.ShouldReuse(first, otherMonitor, false));

        Assert.False(HubPlacement.ShouldReuse(null, first, false));
    }

    [Fact]
    public void Changed_setting_drops_the_old_anchor()
    {
        var auto = HubPlacement.CreateAnchor(HubAnchor.Auto, true, TaskbarSide.Bottom, 1700, 1060, Work, 380, 400);
        var corner = HubPlacement.CreateAnchor(HubAnchor.TopLeft, true, TaskbarSide.Bottom, 1700, 1060, Work, 380, 400);
        Assert.False(HubPlacement.ShouldReuse(auto, corner, false));
    }

    [Fact]
    public void Dragged_pinned_panel_keeps_its_spot_and_grows_towards_the_middle()
    {
        var tray = HubPlacement.CreateAnchor(HubAnchor.Auto, true, TaskbarSide.Bottom, 1700, 1060, Work, 380, 400);

        // Dragged to the upper-left quarter: top-left corner becomes the fixed one.
        var dragged = HubPlacement.FromWindow(new PixelRect(200, 100, 580, 500), Work, tray);
        Assert.True(dragged.Manual);
        Assert.Equal((200, 100), HubPlacement.Place(dragged, 380, 700));

        // Lower-right quarter: bottom-right corner is fixed.
        var lowRight = HubPlacement.FromWindow(new PixelRect(1400, 600, 1780, 1000), Work, tray);
        Assert.Equal((1400, 1000 - 200), HubPlacement.Place(lowRight, 380, 200));

        // Kept while pinned, recalculated from the tray once unpinned.
        Assert.True(HubPlacement.ShouldReuse(dragged, tray, pinned: true));
        Assert.False(HubPlacement.ShouldReuse(dragged, tray, pinned: false));
    }

    [Fact]
    public void Max_height_is_limited_by_the_work_area()
    {
        Assert.Equal(680, HubPlacement.MaxHeightDip(680, Work, 1.0));
        Assert.Equal(520, HubPlacement.MaxHeightDip(680, new PixelRect(0, 0, 1920, 1040), 2.0));
        Assert.Equal(300, HubPlacement.MaxHeightDip(300, Work, 1.0));
    }
}
