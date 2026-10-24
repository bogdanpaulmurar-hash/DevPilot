using System.Windows;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Presets;
using DevPilot.Models;
using DevPilot.UI.Hub;
using DevPilot.UI.Mvvm;
using Hardcodet.Wpf.TaskbarNotification;

namespace DevPilot.UI.Services;

/// <summary>
/// The DevPilot icon in the notification area, plus optional separate icons for tools
/// the user marked "DevPilot tray icon". Everything else lives only in the hub.
/// </summary>
public sealed class TrayManager : IDisposable
{
    private readonly IConfigStore _config;
    private readonly IKeepAwakeService _keepAwake;
    private readonly FlyoutBuilder _flyouts;
    private readonly Dictionary<string, (TaskbarIcon Icon, IconFactory.OwnedIcon Image, string Signature)> _toolIcons = [];
    private TaskbarIcon? _main;
    private IAppShell? _shell;

    public TrayManager(IConfigStore config, IKeepAwakeService keepAwake, FlyoutBuilder flyouts)
    {
        _config = config;
        _keepAwake = keepAwake;
        _flyouts = flyouts;
    }

    public TaskbarIcon? MainIcon => _main;

    public void Initialize(IAppShell shell)
    {
        _shell = shell;
        _main = new TaskbarIcon
        {
            Icon = IconFactory.AppIcon(),
            ToolTipText = "DevPilot",
            LeftClickCommand = new RelayCommand(() => Handle(_config.Current.Hub.LeftClick)),
            DoubleClickCommand = new RelayCommand(() => Handle(_config.Current.Hub.DoubleClick)),
            Visibility = Visibility.Visible,
        };
        _main.TrayRightMouseUp += (_, _) => Handle(_config.Current.Hub.RightClick);
        Notifier.Tray = _main;
        ApplySettings();
        _keepAwake.StateChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(UpdateToolTip);
        _config.Changed += (_, area) =>
        {
            if (area is ConfigArea.All or ConfigArea.Tools or ConfigArea.Hub or ConfigArea.Theme)
                Application.Current.Dispatcher.BeginInvoke(ApplySettings);
        };
    }

    private void ApplySettings()
    {
        if (_main is null) return;
        // Without a double-click action the single click opens instantly.
        _main.NoLeftClickDelay = _config.Current.Hub.DoubleClick == TrayClickAction.Nothing;
        UpdateToolTip();
        SyncToolIcons();
    }

    private void UpdateToolTip()
    {
        if (_main is null) return;
        _main.ToolTipText = _keepAwake.IsActive ? "DevPilot — keep-awake on" : "DevPilot";
    }

    private void Handle(TrayClickAction action)
    {
        if (_shell is null) return;
        switch (action)
        {
            case TrayClickAction.OpenHub: _shell.ToggleHub(); break;
            case TrayClickAction.OpenMainWindow: _shell.HideHub(); _shell.ShowMainWindow(); break;
            case TrayClickAction.QuickMenu: _shell.ShowQuickMenu(); break;
        }
    }

    private void SyncToolIcons()
    {
        var s = _config.Current;
        var wanted = s.Tools.Where(t => t.Tray == TrayMode.DevPilotIcon && !t.Hidden).ToList();
        foreach (var id in _toolIcons.Keys.Except(wanted.Select(t => t.Id)).ToList())
        {
            var (icon, image, _) = _toolIcons[id];
            icon.Dispose();
            image.Dispose();
            _toolIcons.Remove(id);
        }
        foreach (var tool in wanted)
        {
            var signature = $"{tool.Name}|{tool.Glyph}|{tool.Color}|{tool.IconPath}|{s.Accent}";
            if (_toolIcons.TryGetValue(tool.Id, out var existing))
            {
                if (existing.Signature == signature) continue;
                existing.Icon.Dispose();
                existing.Image.Dispose();
                _toolIcons.Remove(tool.Id);
            }
            var image = IconFactory.For(tool, s.Accent);
            var id = tool.Id;
            var icon = new TaskbarIcon
            {
                Icon = image.Icon,
                ToolTipText = tool.Name,
                NoLeftClickDelay = true,
                LeftClickCommand = new RelayCommand(() => ShowToolMenu(id)),
                Visibility = Visibility.Visible,
            };
            icon.TrayRightMouseUp += (_, _) => ShowToolMenu(id);
            _toolIcons[tool.Id] = (icon, image, signature);
        }
    }

    private void ShowToolMenu(string toolId)
    {
        var tool = _config.Current.Tools.FirstOrDefault(t => t.Id == toolId);
        if (tool is null) return;
        _shell?.HideHub();
        ToolMenuWindow.ShowAtCursor(_flyouts.ForTool(tool), tool.Name, tool.Description, _config.Current.Hub);
    }

    public IReadOnlyList<FlyoutItem> QuickMenuItems(IAppShell shell) =>
    [
        FlyoutItem.Action("Open hub", Glyphs.Grid, () => shell.ShowHub(), bold: true),
        FlyoutItem.Action("Open DevPilot", Glyphs.App, () => shell.ShowMainWindow()),
        FlyoutItem.Action(_config.Current.KeepAwake.Enabled ? "Turn keep-awake off" : "Turn keep-awake on", Glyphs.Sun, () =>
        {
            _config.Current.KeepAwake.Enabled = !_config.Current.KeepAwake.Enabled;
            _config.Touch(ConfigArea.KeepAwake);
        }, detail: _keepAwake.IsActive ? "on" : "off"),
        FlyoutItem.Action("Back up now", Glyphs.Save, async () => { await App.BackupNowAsync(); return true; }),
        FlyoutItem.Action("Tools", Glyphs.List, () => shell.ShowMainWindow("tools")),
        FlyoutItem.Action("Settings", Glyphs.Settings, () => shell.ShowMainWindow("settings")),
        FlyoutItem.Separator(),
        new FlyoutItem { Label = "Exit DevPilot", Glyph = Glyphs.Power, IsDanger = true, Invoke = () => { shell.Exit(); return Task.FromResult(true); } },
    ];

    public void Dispose()
    {
        foreach (var (icon, image, _) in _toolIcons.Values)
        {
            icon.Dispose();
            image.Dispose();
        }
        _toolIcons.Clear();
        _main?.Dispose();
        _main = null;
    }
}
