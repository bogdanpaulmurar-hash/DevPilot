using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using DevPilot.Core.Hub;
using DevPilot.Models;
using DevPilot.UI.Services;

namespace DevPilot.UI.Hub;

/// <summary>The panel that opens from the DevPilot tray icon.</summary>
public partial class HubWindow : Window
{
    private readonly HubViewModel _vm;
    private readonly FlyoutController _flyouts = new();
    private bool _fromTray;
    private bool _positioning;
    private HubAnchorPoint? _anchor;

    public HubWindow(HubViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        _flyouts.Invoked += ok => _vm.CloseIfConfigured(ok);
        Deactivated += (_, _) =>
        {
            if (!_vm.IsPinned) HideHub();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (_flyouts.IsOpen) _flyouts.CloseAll();
                else HideHub();
                e.Handled = true;
            }
        };
        DpiChanged += (_, _) =>
        {
            if (IsVisible && !_positioning) Dispatcher.BeginInvoke(Position);
        };
        SizeChanged += (_, _) =>
        {
            // Keep the anchored corner fixed when groups collapse/expand (never re-read the cursor here).
            if (IsVisible && !_positioning) Dispatcher.BeginInvoke(Position);
        };
    }

    public bool IsOpen => IsVisible;

    public void ShowHub(bool fromTray, bool pinned = false)
    {
        _fromTray = fromTray;
        if (pinned) _vm.IsPinned = true;
        ApplyFlyoutLook();
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }
        UpdateAnchor();
        Position();
        Opacity = _vm.PanelOpacity;
        Activate();
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.SetForegroundWindow(hwnd);
        if (_vm.ShowSearch)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }
        _ = _vm.RefreshRecentAsync();
    }

    public void HideHub()
    {
        _flyouts.CloseAll();
        if (!IsVisible) return;
        Hide();
        _vm.SearchText = string.Empty;
    }

    private void ApplyFlyoutLook()
    {
        var look = _vm.Look;
        _flyouts.Width = look.FlyoutWidth;
        _flyouts.FontSize = look.FontSize;
        _flyouts.HoverDelayMs = look.HoverDelayMs;
    }

    /// <summary>
    /// Decides where the panel is anchored when it opens: next to the tray (or the configured corner) on the
    /// monitor under the cursor. A repeated click on the same tray icon keeps the previous anchor, so the
    /// panel reopens in exactly the same place.
    /// </summary>
    private void UpdateAnchor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var (_, workRect) = Native.MonitorAtCursor();
        var work = ToPixelRect(workRect);
        ApplyMaxHeight(work);
        UpdateLayout();
        Native.GetWindowRect(hwnd, out var r);
        var cursor = Native.CursorPixels();
        var candidate = HubPlacement.CreateAnchor(_vm.Look.Anchor, _fromTray, CurrentTaskbarSide(),
            (int)cursor.X, (int)cursor.Y, work, r.Width, r.Height);
        if (!HubPlacement.ShouldReuse(_anchor, candidate, _vm.IsPinned)) _anchor = candidate;
    }

    /// <summary>Applies the anchor for the current size, in device pixels so mixed-DPI monitors work.</summary>
    private void Position()
    {
        if (_anchor is null) return;
        _positioning = true;
        try
        {
            ApplyMaxHeight(_anchor.Work);
            UpdateLayout();
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            Native.GetWindowRect(hwnd, out var r);
            int w = r.Width, h = r.Height;
            var (x, y) = HubPlacement.Place(_anchor, w, h);
            if (x != r.Left || y != r.Top)
                Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);

            var work = _anchor.Work;
            var side = _vm.Look.FlyoutSide;
            if (side == FlyoutSide.Auto)
                side = x + w / 2 > work.Left + work.Width / 2 ? FlyoutSide.Left : FlyoutSide.Right;
            _flyouts.Side = side;
        }
        finally
        {
            _positioning = false;
        }
    }

    /// <summary>Never taller than the work area: the group list scrolls instead of the panel leaving the screen.</summary>
    private void ApplyMaxHeight(PixelRect work)
    {
        var scale = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleY;
        var max = HubPlacement.MaxHeightDip(_vm.PanelMaxHeight, work, scale);
        if (Math.Abs(MaxHeight - max) > 0.5) SetCurrentValue(MaxHeightProperty, max);
    }

    /// <summary>After the user drags the pinned panel, that spot becomes its anchor.</summary>
    private void RememberDraggedPosition()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        Native.GetWindowRect(hwnd, out var r);
        var (_, work) = Native.MonitorOfWindow(hwnd);
        _anchor = HubPlacement.FromWindow(ToPixelRect(r), ToPixelRect(work), _anchor);
        Position();
    }

    private static PixelRect ToPixelRect(Native.RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);

    private static TaskbarSide CurrentTaskbarSide() => Native.TaskbarEdge() switch
    {
        0 => TaskbarSide.Left,
        1 => TaskbarSide.Top,
        2 => TaskbarSide.Right,
        _ => TaskbarSide.Bottom,
    };

    // ── Rows ──────────────────────────────────────────────────────────────
    private void ToolRow_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not HubToolVM tool) return;
        if (!tool.HasFlyout) { _flyouts.Schedule(null, null); return; }
        _flyouts.Schedule(fe, () => _vm.Flyouts.ForTool(tool.Model), tool.Name, Subtitle(tool));
    }

    private static string Subtitle(HubToolVM tool) =>
        string.IsNullOrWhiteSpace(tool.StatusText) ? tool.Description : tool.StatusText;

    private async void ToolRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not HubToolVM tool) return;
        e.Handled = true;
        _flyouts.CloseAll();
        await _vm.ActivateAsync(tool);
    }

    private void ToolRow_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not HubToolVM tool) return;
        e.Handled = true;
        _flyouts.OpenNow(fe, _vm.Flyouts.ForTool(tool.Model), tool.Name, Subtitle(tool));
    }

    private void GroupHeader_MouseEnter(object sender, MouseEventArgs e) => _flyouts.Schedule(null, null);

    private void SpotHeader_MouseEnter(object sender, MouseEventArgs e)
    {
        var sp = _vm.Spotlight;
        if (sp is null) return;
        _flyouts.Schedule(SpotHeader, () => _vm.Flyouts.ForTool(sp.Tool.Model), sp.Tool.Name, Subtitle(sp.Tool));
    }

    private void SpotHeader_Click(object sender, MouseButtonEventArgs e)
    {
        var sp = _vm.Spotlight;
        if (sp is null || e.OriginalSource is DependencyObject d && FindParent<Button>(d) is not null) return;
        _flyouts.OpenNow(SpotHeader, _vm.Flyouts.ForTool(sp.Tool.Model), sp.Tool.Name, Subtitle(sp.Tool));
    }

    private void GroupsScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0) _flyouts.CloseAll();
    }

    private async void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm.FirstMatch is { } first)
        {
            e.Handled = true;
            await _vm.ActivateAsync(first);
        }
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm.IsPinned && e.ButtonState == MouseButtonState.Pressed)
        {
            _flyouts.CloseAll();
            try { DragMove(); } catch (InvalidOperationException) { return; }
            RememberDraggedPosition();
        }
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var current = child;
        while (current is not null)
        {
            if (current is T t) return t;
            current = current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }
}
