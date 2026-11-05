using DevPilot.Models;

namespace DevPilot.UI.Services;

/// <summary>Navigation between the tray hub, the main window and app lifetime.</summary>
public interface IAppShell
{
    void ShowMainWindow(string? page = null);
    void EditTool(ToolEntry tool);
    void ToggleHub();
    void ShowHub(bool pinned = false);
    void HideHub();
    void ShowQuickMenu();
    /// <summary>The repositories panel (tick, pull, run a repository's commands).</summary>
    void ShowRepoPanel();
    void Exit();
}
