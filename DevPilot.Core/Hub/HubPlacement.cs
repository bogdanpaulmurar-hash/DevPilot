using DevPilot.Models;

namespace DevPilot.Core.Hub;

/// <summary>A rectangle in device pixels (left/top inclusive, right/bottom exclusive).</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>Screen edge the taskbar sits on.</summary>
public enum TaskbarSide { Left, Top, Right, Bottom }

/// <summary>
/// Where the hub is pinned on screen: a reference point plus the window corner that sits on it.
/// The point is fixed for as long as the anchor lives, so the panel grows away from it when
/// groups expand instead of following the mouse.
/// </summary>
public sealed record HubAnchorPoint(
    int X, int Y,
    bool AlignRight, bool AlignBottom,
    PixelRect Work,
    HubAnchor Mode, TaskbarSide Taskbar,
    int CursorX, int CursorY,
    bool Manual = false);

/// <summary>Pure placement logic for the tray hub (no WPF, so it can be unit tested).</summary>
public static class HubPlacement
{
    /// <summary>How far past the cursor the panel extends on the tray side, so it sits over the icon.</summary>
    public const int TrayOverhang = 60;

    /// <summary>Tray clicks within this distance (px) of the previous one reuse the same anchor.</summary>
    public const int ReuseTolerance = 48;

    /// <summary>Builds the anchor when the hub opens. <paramref name="width"/>/<paramref name="height"/> are only used to centre it.</summary>
    public static HubAnchorPoint CreateAnchor(HubAnchor configured, bool fromTray, TaskbarSide taskbar,
        int cursorX, int cursorY, PixelRect work, int width, int height)
    {
        var mode = configured == HubAnchor.Auto && !fromTray ? HubAnchor.Center : configured;
        HubAnchorPoint Make(int x, int y, bool right, bool bottom) =>
            new(x, y, right, bottom, work, mode, taskbar, cursorX, cursorY);

        return mode switch
        {
            HubAnchor.BottomRight => Make(work.Right, work.Bottom, true, true),
            HubAnchor.BottomLeft => Make(work.Left, work.Bottom, false, true),
            HubAnchor.TopRight => Make(work.Right, work.Top, true, false),
            HubAnchor.TopLeft => Make(work.Left, work.Top, false, false),
            // Centre for the first size, then keep the top edge fixed while it grows downwards.
            HubAnchor.Center => Make(work.Left + (work.Width - width) / 2, work.Top + (work.Height - height) / 2, false, false),
            // Auto from the tray: corner next to the tray stays put, panel grows away from the taskbar.
            _ => taskbar switch
            {
                TaskbarSide.Left => Make(work.Left, cursorY + TrayOverhang, false, true),
                TaskbarSide.Top => Make(cursorX + TrayOverhang, work.Top, true, false),
                TaskbarSide.Right => Make(work.Right, cursorY + TrayOverhang, true, true),
                _ => Make(cursorX + TrayOverhang, work.Bottom, true, true),
            },
        };
    }

    /// <summary>
    /// True when a new opening should keep the previous anchor: same monitor and settings, and (for the
    /// tray) a click on the same icon. A manual anchor (dragged while pinned) is kept while still pinned.
    /// </summary>
    public static bool ShouldReuse(HubAnchorPoint? previous, HubAnchorPoint candidate, bool pinned)
    {
        if (previous is null) return false;
        if (previous.Manual) return pinned;
        if (previous.Work != candidate.Work || previous.Mode != candidate.Mode || previous.Taskbar != candidate.Taskbar)
            return false;
        if (candidate.Mode != HubAnchor.Auto) return true;
        return Math.Abs(previous.CursorX - candidate.CursorX) <= ReuseTolerance
            && Math.Abs(previous.CursorY - candidate.CursorY) <= ReuseTolerance;
    }

    /// <summary>Top-left position for a window of the given size, kept inside the work area.</summary>
    public static (int X, int Y) Place(HubAnchorPoint anchor, int width, int height)
    {
        var work = anchor.Work;
        int x = anchor.AlignRight ? anchor.X - width : anchor.X;
        int y = anchor.AlignBottom ? anchor.Y - height : anchor.Y;
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));
        return (x, y);
    }

    /// <summary>
    /// Anchor for a window the user dragged (pinned hub): the corner nearest to the closest screen edges
    /// becomes the fixed one, so expanding a group grows the panel towards the middle of the screen.
    /// </summary>
    public static HubAnchorPoint FromWindow(PixelRect window, PixelRect work, HubAnchorPoint? previous)
    {
        bool right = window.Left + window.Width / 2 > work.Left + work.Width / 2;
        bool bottom = window.Top + window.Height / 2 > work.Top + work.Height / 2;
        return new HubAnchorPoint(
            right ? window.Right : window.Left,
            bottom ? window.Bottom : window.Top,
            right, bottom, work,
            previous?.Mode ?? HubAnchor.Auto, previous?.Taskbar ?? TaskbarSide.Bottom,
            previous?.CursorX ?? 0, previous?.CursorY ?? 0,
            Manual: true);
    }

    /// <summary>Maximum panel height in DIPs: the configured limit, but never taller than the work area.</summary>
    public static double MaxHeightDip(double configuredMax, PixelRect work, double dpiScale) =>
        Math.Min(configuredMax, Math.Max(120, work.Height / (dpiScale <= 0 ? 1 : dpiScale)));
}
