// ============================================================
// DevPilot — IT Automation Hub v1.0
// MainViewModel.cs — Shell ViewModel
// Phase: 9 (+SettingsViewModel)
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

namespace DevPilot.UI.ViewModels;

public sealed class MainViewModel : BaseViewModel
{
    public AppLauncherViewModel  AppLauncherViewModel  { get; }
    public JigglerViewModel      JigglerViewModel      { get; }
    public ScreenshotViewModel   ScreenshotViewModel   { get; }
    public HeartbeatViewModel    HeartbeatViewModel    { get; }
    public SettingsViewModel     SettingsViewModel     { get; }

    public MainViewModel(
        AppLauncherViewModel  appLauncherViewModel,
        JigglerViewModel      jigglerViewModel,
        ScreenshotViewModel   screenshotViewModel,
        HeartbeatViewModel    heartbeatViewModel,
        SettingsViewModel     settingsViewModel)
    {
        AppLauncherViewModel = appLauncherViewModel;
        JigglerViewModel     = jigglerViewModel;
        ScreenshotViewModel  = screenshotViewModel;
        HeartbeatViewModel   = heartbeatViewModel;
        SettingsViewModel    = settingsViewModel;
    }
}