using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using DevPilot.Models;
using DevPilot.UI.Services;
using DevPilot.UI.ViewModels;

namespace DevPilot.UI.Hub;

/// <summary>
/// Opens from the hub's sync button: the repositories with a tick each, "Pull selected", live results and one
/// button per row. It stays open (it is not closed by clicking elsewhere) so a build can be followed; Esc or X closes it.
/// </summary>
public partial class RepoPanelWindow : Window
{
    private int _anchorRight;
    private int _anchorBottom;
    private bool _placing;

    public RepoPanelWindow(ReposViewModel vm, HubAppearance look)
    {
        InitializeComponent();
        DataContext = vm;
        FontSize = look.FontSize;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
        // The list fills in after the scan: keep the bottom-right corner where the user clicked.
        SizeChanged += (_, _) => { if (IsVisible && !_placing) Dispatcher.BeginInvoke(Place); };
    }

    public void ShowPanel(ReposViewModel vm)
    {
        vm.OnShown();
        Opacity = 0;
        Show();
        var c = Native.CursorPixels();
        _anchorRight = (int)c.X + 12;
        _anchorBottom = (int)c.Y + 12;
        Place();
        Opacity = 1;
        Activate();
        Native.SetForegroundWindow(new WindowInteropHelper(this).Handle);
    }

    private void Place()
    {
        _placing = true;
        try
        {
            UpdateLayout();
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            Native.GetWindowRect(hwnd, out var r);
            var (_, work) = Native.MonitorAtCursor();
            int x = Math.Clamp(_anchorRight - r.Width, work.Left, Math.Max(work.Left, work.Right - r.Width));
            int y = Math.Clamp(_anchorBottom - r.Height, work.Top, Math.Max(work.Top, work.Bottom - r.Height));
            Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }
        finally { _placing = false; }
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not (FrameworkElement { } el) || el is System.Windows.Controls.Primitives.ButtonBase) return;
        _placing = true;
        try { DragMove(); } catch (InvalidOperationException) { }
        finally { _placing = false; }
        // Remember the dragged position so a later resize keeps it.
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.GetWindowRect(hwnd, out var r);
        _anchorRight = r.Left + r.Width;
        _anchorBottom = r.Top + r.Height;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
