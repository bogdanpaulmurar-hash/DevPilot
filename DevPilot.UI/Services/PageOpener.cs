using System.Windows;
using System.Windows.Threading;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Presets;
using DevPilot.Models;
using DevPilot.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DevPilot.UI.Services;

/// <summary>
/// Opens DevPilot's own pages for a tool's <see cref="ActionKind.Page"/> options. The page opens after the hub
/// flyout or tray menu that was clicked has closed (never from inside it), in the main window.
/// </summary>
public sealed class PageOpener : IPageOpener
{
    private readonly IServiceProvider _services;

    public PageOpener(IServiceProvider services) => _services = services;

    public bool Open(string page, ToolEntry tool)
    {
        if (!string.Equals(page, ToolPresets.ZonePilotPage, StringComparison.OrdinalIgnoreCase)) return false;
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _services.GetRequiredService<ZonePilotViewModel>().UseTool(tool);
            var shell = _services.GetRequiredService<IAppShell>();
            shell.HideHub();
            shell.ShowMainWindow(ToolPresets.ZonePilotPage);
        }, DispatcherPriority.Background);
        return true;
    }
}
