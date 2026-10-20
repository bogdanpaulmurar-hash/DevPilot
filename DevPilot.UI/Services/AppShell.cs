using System.Windows;
using DevPilot.Core.Interfaces;
using DevPilot.Models;
using DevPilot.UI.Hub;
using DevPilot.UI.ViewModels;
using DevPilot.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DevPilot.UI.Services;

/// <summary>Owns the hub window and the main window, and the app's exit path.</summary>
public sealed class AppShell : IAppShell
{
    private readonly IServiceProvider _services;
    private HubWindow? _hub;
    private MainWindow? _main;
    private DateTime _hubHiddenAt = DateTime.MinValue;

    public AppShell(IServiceProvider services) => _services = services;

    private HubWindow Hub => _hub ??= CreateHub();

    private HubWindow CreateHub()
    {
        var w = new HubWindow(_services.GetRequiredService<HubViewModel>());
        w.IsVisibleChanged += (_, _) => { if (!w.IsVisible) _hubHiddenAt = DateTime.UtcNow; };
        return w;
    }

    private MainWindow Main
    {
        get
        {
            if (_main is null)
            {
                _main = new MainWindow(_services.GetRequiredService<MainViewModel>(), _services.GetRequiredService<IConfigStore>());
            }
            return _main;
        }
    }

    public void ShowMainWindow(string? page = null)
    {
        var w = Main;
        w.ViewModel.Navigate(page);
        if (!w.IsVisible) w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
        w.Topmost = true;   // bring to front reliably, then release
        w.Topmost = false;
        w.Focus();
    }

    public void EditTool(ToolEntry tool)
    {
        HideHub();
        ShowMainWindow("tools");
        _services.GetRequiredService<ToolsViewModel>().Select(tool);
    }

    public void ToggleHub()
    {
        // A click on the tray icon first deactivates (and hides) the hub; don't reopen it immediately.
        if (_hub is { IsVisible: true }) { HideHub(); return; }
        if (DateTime.UtcNow - _hubHiddenAt < TimeSpan.FromMilliseconds(300)) return;
        Hub.ShowHub(fromTray: true);
    }

    public void ShowHub(bool pinned = false) => Hub.ShowHub(fromTray: !pinned, pinned: pinned);

    public void HideHub() => _hub?.HideHub();

    public void ShowQuickMenu()
    {
        HideHub();
        var tray = _services.GetRequiredService<TrayManager>();
        var look = _services.GetRequiredService<IConfigStore>().Current.Hub;
        ToolMenuWindow.ShowAtCursor(tray.QuickMenuItems(this), "DevPilot", null, look);
    }

    public void Exit()
    {
        if (_main is not null) _main.AllowClose = true;
        _hub?.HideHub();
        if (Application.Current is App app) app.Cleanup();
        Application.Current.Shutdown();
    }
}
