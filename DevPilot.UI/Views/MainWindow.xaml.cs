// ============================================================
// DevPilot — IT Automation Hub v1.0
// MainWindow.xaml.cs — Shell window code-behind
// Phase: 9 (+SettingsViewModel, +ThemeManager, +MinimizeToTray)
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Windows;
using System.Windows.Interop;
using DevPilot.Core.Interfaces;
using DevPilot.Models;
using DevPilot.UI.ViewModels;
using DevPilot.UI.Views.Dialogs;

namespace DevPilot.UI.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel     _vm;
    private readonly SettingsViewModel _settingsVm;
    private readonly IHotkeyManager    _hotkeyManager;
    private readonly ISettingsManager  _settings;
    private readonly JigglerViewModel  _jigglerVm;
    private readonly ScreenshotViewModel _screenshotVm;
    private bool _forceClose = false;

    public MainWindow(
        MainViewModel        mainVm,
        AppLauncherViewModel launcherVm,
        ExternalToolsViewModel externalToolsVm,
        JigglerViewModel     jigglerVm,
        ScreenshotViewModel  screenshotVm,
        HeartbeatViewModel   heartbeatVm,
        SettingsViewModel    settingsVm,
        IHotkeyManager       hotkeyManager,
        ISettingsManager     settings)
    {
        InitializeComponent();

        _vm            = mainVm;
        _settingsVm    = settingsVm;
        _jigglerVm     = jigglerVm;
        _screenshotVm  = screenshotVm;
        _hotkeyManager = hotkeyManager;
        _settings      = settings;
        DataContext    = _vm;

        // Inject DataContext into sub-views
        AppLauncherViewControl.DataContext  = launcherVm;
        ExternalToolsViewControl.DataContext = externalToolsVm;
        JigglerViewControl.DataContext      = jigglerVm;
        ScreenshotViewControl.DataContext   = screenshotVm;
        HeartbeatViewControl.DataContext    = heartbeatVm;
        SettingsViewControl.DataContext     = settingsVm;

        // Wire the predefined hotkey picker into SettingsViewModel
        settingsVm.ShowHotkeyPickerDialog = async (actionLabel, currentMods, currentKey) =>
        {
            var dlg = new HotkeyPickerDialog(actionLabel, currentMods, currentKey) { Owner = this };
            return dlg.ShowDialog() == true ? dlg.Result : null;
        };

        // Re-register hotkeys immediately after every Save
        settingsVm.HotkeysChanged += (_, _) => ReRegisterHotkeys();

        // Wire dialog for AppLauncher add/edit (unchanged from Phase 2)
        launcherVm.ShowAddEditDialog = async existing =>
        {
            var dlg = new AddAppDialog(existing) { Owner = this };
            dlg.ShowDialog();
            return dlg.Result;
        };

        // Wire dialog for custom tool add (Stage 10B)
        externalToolsVm.ShowAddToolDialog = () =>
        {
            var dlg = new AddToolDialog { Owner = this };
            return dlg.ShowDialog() == true
                ? (dlg.ResultName, dlg.ResultDescription, dlg.ResultIcon, dlg.ResultExePath)
                : null;
        };

        // Wire dialog for custom tool edit
        externalToolsVm.ShowEditToolDialog = existing =>
        {
            var dlg = new AddToolDialog(existing) { Owner = this };
            return dlg.ShowDialog() == true
                ? (dlg.ResultName, dlg.ResultDescription, dlg.ResultIcon, dlg.ResultExePath)
                : null;
        };

        Loaded += OnLoaded;
    }

    // ── OnLoaded — apply theme from settings + register hotkeys ──────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Apply persisted theme before showing window
        ThemeManager.Apply(_settingsVm.IsDarkTheme);

        var hwnd = new WindowInteropHelper(this).Handle;
        _hotkeyManager.Initialize(hwnd, action => Dispatcher.InvokeAsync(action));
        HwndSource.FromHwnd(hwnd).AddHook(_hotkeyManager.WndProcHook);

        // Register hotkeys from persisted settings (LoadFromSettings already applied defaults)
        ReRegisterHotkeys();
    }

    // ── Hotkey handler ────────────────────────────────────────────────────

    private void OnHotkeyPressed(object? sender, string action)
    {
        switch (action)
        {
            case AppConstants.HotkeyActionShowHide:
                if (IsVisible) Hide(); else { Show(); Activate(); }
                break;

            case AppConstants.HotkeyActionToggleJiggler:
                _jigglerVm.ToggleCommand.Execute(null);
                break;

            case AppConstants.HotkeyActionScreenshot:
                if (_screenshotVm.CaptureFullScreenCommand.CanExecute(null))
                    _screenshotVm.CaptureFullScreenCommand.Execute(null);
                break;
        }
    }

    // ── Theme toggle (header button) ──────────────────────────────────────

    private void OnThemeToggle(object sender, RoutedEventArgs e)
    {
        // Delegate to SettingsViewModel — it handles persistence + ThemeManager
        _settingsVm.IsDarkTheme = !_settingsVm.IsDarkTheme;
    }

    // ── Close / Minimize to tray ──────────────────────────────────────────

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_forceClose)
        {
            if (_settings.Current.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            // MinimizeToTray = false → allow window to close → app shuts down
            _forceClose = true;
        }

        _hotkeyManager.UnregisterAll();
        _hotkeyManager.Dispose();
        base.OnClosing(e);
    }

    /// <summary>Called by TrayViewModel.ExitCommand to force a clean shutdown.</summary>
    internal void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    // ── Hotkey registration ───────────────────────────────────────────────

    private void ReRegisterHotkeys()
    {
        _hotkeyManager.UnregisterAll();
        foreach (var row in _settingsVm.Hotkeys)
        {
            if (!row.IsEnabled) continue;
            _hotkeyManager.Register(new HotkeyConfig
            {
                Id        = row.Id,
                Action    = row.ActionKey,
                Modifiers = row.Modifiers,
                Key       = row.Key,
                IsEnabled = true,
            });
        }
    }
}