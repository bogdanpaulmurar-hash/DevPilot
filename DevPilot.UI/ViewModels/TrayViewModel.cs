// ============================================================
// DevPilot — IT Automation Hub v1.0
// TrayViewModel.cs — ViewModel for System Tray icon & menu
// Phase: 10 (Fix: plain-text menu labels — emoji fails in Win32 menus)
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Windows;
using System.Windows.Input;
using DevPilot.UI.Commands;

namespace DevPilot.UI.ViewModels;

/// <summary>
/// ViewModel for the system tray icon and context menu.
/// DataContext is set on the TaskbarIcon in App.xaml.cs after FindResource.
/// </summary>
public class TrayViewModel : BaseViewModel
{
    private readonly JigglerViewModel _jiggler;

    // ── Properties ───────────────────────────────────────────────────────

    /// <summary>Context-menu label for the Jiggler toggle item. Updates when jiggler state changes.</summary>
    public string JigglerMenuLabel => _jiggler.IsRunning
        ? "Stop Jiggler  (running)"
        : "Start Jiggler";

    // ── Commands ─────────────────────────────────────────────────────────

    /// <summary>Restores and activates the main window.</summary>
    public ICommand ShowWindowCommand    { get; }

    /// <summary>Toggles the Mouse Jiggler on/off.</summary>
    public ICommand ToggleJigglerCommand { get; }

    /// <summary>Shuts down DevPilot cleanly.</summary>
    public ICommand ExitCommand          { get; }

    // ── Constructor ──────────────────────────────────────────────────────

    public TrayViewModel(JigglerViewModel jiggler)
    {
        _jiggler = jiggler;

        // Re-raise JigglerMenuLabel when jiggler running state changes
        _jiggler.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(JigglerViewModel.IsRunning))
                OnPropertyChanged(nameof(JigglerMenuLabel));
        };

        ShowWindowCommand = new RelayCommand(_ =>
        {
            var win = Application.Current.MainWindow;
            if (win is null) return;
            win.Show();
            win.WindowState = WindowState.Normal;
            win.Activate();
        });

        ToggleJigglerCommand = new RelayCommand(_ =>
            _jiggler.ToggleCommand.Execute(null));

        ExitCommand = new RelayCommand(_ =>
            Application.Current.Shutdown());
    }
}
