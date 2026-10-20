using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using DevPilot.Models;
using DevPilot.UI.Services;

namespace DevPilot.UI.Hub;

/// <summary>Compact menu at the cursor: the DevPilot quick menu and the menus of tools with their own DevPilot tray icon.</summary>
public partial class ToolMenuWindow : Window
{
    private readonly FlyoutController _flyouts = new();
    private bool _closing;

    public ToolMenuWindow()
    {
        InitializeComponent();
        _flyouts.Invoked += _ => SafeClose();
        Deactivated += (_, _) => SafeClose();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) SafeClose(); };
        Closing += (_, _) => _closing = true;
        Closed += (_, _) => _flyouts.CloseAll();
    }

    private void SafeClose()
    {
        if (_closing) return;
        _closing = true;
        Dispatcher.BeginInvoke(Close);
    }

    public static void ShowAtCursor(IReadOnlyList<FlyoutItem> items, string? title, string? subtitle, HubAppearance look)
    {
        var w = new ToolMenuWindow();
        var panel = new FlyoutPanel(0, items, Math.Max(220, look.FlyoutWidth), look.FontSize, title, subtitle);
        w._flyouts.Width = look.FlyoutWidth;
        w._flyouts.FontSize = look.FontSize;
        w._flyouts.HoverDelayMs = look.HoverDelayMs;
        w._flyouts.AttachHosted(panel);
        w.Host.Content = panel;
        w.Opacity = 0;
        w.Show();
        w.PlaceAtCursor();
        w.Opacity = 1;
        w.Activate();
        Native.SetForegroundWindow(new WindowInteropHelper(w).Handle);
    }

    private void PlaceAtCursor()
    {
        UpdateLayout();
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.GetWindowRect(hwnd, out var r);
        var (_, work) = Native.MonitorAtCursor();
        var c = Native.CursorPixels();
        int x = (int)c.X - r.Width + 12;
        int y = (int)c.Y - r.Height + 12;
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - r.Width));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - r.Height));
        Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        _flyouts.Side = x + r.Width / 2 > work.Left + work.Width / 2 ? FlyoutSide.Left : FlyoutSide.Right;
    }
}
