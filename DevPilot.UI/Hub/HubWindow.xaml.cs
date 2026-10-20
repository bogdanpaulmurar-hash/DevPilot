using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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
        SizeChanged += (_, e) =>
        {
            // Keep the bottom edge anchored when groups collapse/expand.
            if (IsVisible && !_positioning && e.HeightChanged && !_vm.IsPinned) Dispatcher.BeginInvoke(Position);
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

    /// <summary>Places the panel near the tray (or where the user anchored it), in device pixels so mixed-DPI monitors work.</summary>
    private void Position()
    {
        _positioning = true;
        try
        {
            UpdateLayout();
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            Native.GetWindowRect(hwnd, out var r);
            int w = r.Width, h = r.Height;
            var (_, work) = Native.MonitorAtCursor();
            var cursor = Native.CursorPixels();
            var anchor = _vm.Look.Anchor;
            if (anchor == HubAnchor.Auto && !_fromTray) anchor = HubAnchor.Center;

            int x, y;
            switch (anchor)
            {
                case HubAnchor.BottomRight: x = work.Right - w; y = work.Bottom - h; break;
                case HubAnchor.BottomLeft: x = work.Left; y = work.Bottom - h; break;
                case HubAnchor.TopRight: x = work.Right - w; y = work.Top; break;
                case HubAnchor.TopLeft: x = work.Left; y = work.Top; break;
                case HubAnchor.Center: x = work.Left + (work.Width - w) / 2; y = work.Top + (work.Height - h) / 2; break;
                default:
                    // Next to the cursor, on the side of the taskbar.
                    switch (Native.TaskbarEdge())
                    {
                        case 0: x = work.Left; y = (int)cursor.Y - h / 2; break;
                        case 1: x = (int)cursor.X - w + 60; y = work.Top; break;
                        case 2: x = work.Right - w; y = (int)cursor.Y - h / 2; break;
                        default: x = (int)cursor.X - w + 60; y = work.Bottom - h; break;
                    }
                    break;
            }
            x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - w));
            y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - h));
            Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);

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
            try { DragMove(); } catch (InvalidOperationException) { }
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
