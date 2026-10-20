using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using DevPilot.Models;

namespace DevPilot.UI.Hub;

/// <summary>
/// Opens cascading flyouts (popups) next to an anchor: tool options on hover, nested folder content.
/// One hover timer serialises intentions so moving the mouse diagonally does not flicker.
/// </summary>
public sealed class FlyoutController
{
    private readonly List<(Popup? Popup, FlyoutPanel Panel)> _levels = [];
    private readonly DispatcherTimer _timer;
    private Action? _pending;

    public double Width { get; set; } = 260;
    public double FontSize { get; set; } = 13;
    public int HoverDelayMs { get; set; } = 250;
    /// <summary>Resolved side (Left or Right). Auto is resolved by the host.</summary>
    public FlyoutSide Side { get; set; } = FlyoutSide.Left;

    /// <summary>An item was invoked; argument tells whether the host may close (launch succeeded).</summary>
    public event Action<bool>? Invoked;

    public FlyoutController()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            var p = _pending;
            _pending = null;
            p?.Invoke();
        };
    }

    public bool IsOpen => _levels.Count > 0;
    public bool IsMouseOverAny => _levels.Any(l => l.Panel.IsMouseOver);
    public FrameworkElement? RootAnchor { get; private set; }

    /// <summary>Schedules (after the hover delay) a root flyout for an anchor; null items = close everything.</summary>
    public void Schedule(FrameworkElement? anchor, Func<IReadOnlyList<FlyoutItem>>? items, string? title = null, string? subtitle = null)
    {
        if (anchor is not null && ReferenceEquals(anchor, RootAnchor) && IsOpen)
        {
            Cancel();
            CloseFrom(1);
            return;
        }
        Plan(() =>
        {
            if (anchor is null || items is null) CloseAll();
            else OpenLevel(0, anchor, items(), title, subtitle);
        }, HoverDelayMs);
    }

    public void OpenNow(FrameworkElement anchor, IReadOnlyList<FlyoutItem> items, string? title = null, string? subtitle = null)
    {
        Cancel();
        OpenLevel(0, anchor, items, title, subtitle);
    }

    public void Cancel()
    {
        _timer.Stop();
        _pending = null;
    }

    private void Plan(Action action, int delayMs)
    {
        _timer.Stop();
        _pending = action;
        if (delayMs <= 0) { _pending = null; action(); return; }
        _timer.Interval = TimeSpan.FromMilliseconds(delayMs);
        _timer.Start();
    }

    private void OpenLevel(int level, FrameworkElement anchor, IReadOnlyList<FlyoutItem> items, string? title = null, string? subtitle = null)
    {
        CloseFrom(level);
        if (items.Count == 0) return;
        var panel = new FlyoutPanel(level, items, Width, FontSize, title, subtitle);
        panel.ItemHovered += OnItemHovered;
        panel.ItemInvoked += OnItemInvoked;
        var popup = new Popup
        {
            Child = panel,
            PlacementTarget = anchor,
            Placement = Side == FlyoutSide.Right ? PlacementMode.Right : PlacementMode.Left,
            HorizontalOffset = Side == FlyoutSide.Right ? -2 : 2,
            VerticalOffset = level == 0 && title is not null ? -40 : -10,
            AllowsTransparency = true,
            StaysOpen = true,
            PopupAnimation = PopupAnimation.Fade,
            Focusable = false,
        };
        if (level == 0) RootAnchor = anchor;
        _levels.Add((popup, panel));
        popup.IsOpen = true;
    }

    private void OnItemHovered(FlyoutPanel panel, FlyoutItem item, FrameworkElement element)
    {
        int next = panel.Level + 1;
        if (item.HasChildren)
        {
            if (_levels.Count > next && ReferenceEquals(_levels[next].Popup.PlacementTarget, element)) { Cancel(); CloseFrom(next + 1); return; }
            Plan(() => OpenLevel(next, element, item.Children!()), HoverDelayMs);
        }
        else if (_levels.Count > next)
        {
            Plan(() => CloseFrom(next), HoverDelayMs + 100);
        }
        else Cancel();
    }

    private async void OnItemInvoked(FlyoutPanel panel, FlyoutItem item, FrameworkElement element)
    {
        if (item.Invoke is null)
        {
            if (item.HasChildren) OpenLevel(panel.Level + 1, element, item.Children!());
            return;
        }
        Cancel();
        bool ok;
        try { ok = await item.Invoke(); }
        catch (Exception ex)
        {
            Services.Notifier.Error(item.Label, ex.Message);
            ok = false;
        }
        Invoked?.Invoke(ok);
    }

    /// <summary>Uses a panel hosted in a window (tool menus) as level 0; nested levels open as popups.</summary>
    public void AttachHosted(FlyoutPanel panel)
    {
        CloseAll();
        panel.ItemHovered += OnItemHovered;
        panel.ItemInvoked += OnItemInvoked;
        _levels.Add((null, panel));
    }

    public void CloseFrom(int level)
    {
        for (int i = _levels.Count - 1; i >= level; i--)
        {
            if (_levels[i].Popup is { } popup) popup.IsOpen = false;
            _levels[i].Panel.ItemHovered -= OnItemHovered;
            _levels[i].Panel.ItemInvoked -= OnItemInvoked;
            _levels.RemoveAt(i);
        }
        if (_levels.Count == 0) RootAnchor = null;
    }

    public void CloseAll()
    {
        Cancel();
        CloseFrom(0);
    }
}
