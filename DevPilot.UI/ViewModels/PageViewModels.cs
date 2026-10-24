using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using DevPilot.Core.Config;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Launch;
using DevPilot.Core.Presets;
using DevPilot.Core.Startup;
using DevPilot.Infrastructure.Services;
using DevPilot.Models;
using DevPilot.UI.Hub;
using DevPilot.UI.Mvvm;
using DevPilot.UI.Services;
using Microsoft.Win32;

namespace DevPilot.UI.ViewModels;

// ════════════════════════════════════════════════════════════════════════
// Keep-awake
// ════════════════════════════════════════════════════════════════════════

public sealed class DayToggleVM : ObservableObject
{
    private readonly KeepAwakeSettings _s;
    private readonly IConfigStore _config;
    public string Label { get; }
    public int Day { get; }

    public DayToggleVM(KeepAwakeSettings s, IConfigStore config, int day, string label)
    {
        _s = s; _config = config; Day = day; Label = label;
    }

    public bool IsOn
    {
        get => _s.ScheduleDays.Contains(Day);
        set
        {
            if (value == IsOn) return;
            if (value) _s.ScheduleDays.Add(Day); else _s.ScheduleDays.Remove(Day);
            OnPropertyChanged();
            _config.Touch(ConfigArea.KeepAwake);
        }
    }
}

public sealed record OverlapInfo(string Tool, string Text);

public sealed class KeepAwakeViewModel : ObservableObject, IPage
{
    private readonly IConfigStore _config;
    private readonly IKeepAwakeService _service;

    public ObservableCollection<DayToggleVM> Days { get; } = [];
    public ObservableCollection<OverlapInfo> Overlaps { get; } = [];

    public KeepAwakeViewModel(IConfigStore config, IKeepAwakeService service)
    {
        _config = config;
        _service = service;
        _service.StateChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(Status)));
        _config.Changed += (_, a) => { if (a == ConfigArea.All) Application.Current.Dispatcher.BeginInvoke(OnShown); };
        OnShown();
    }

    public KeepAwakeSettings Settings => _config.Current.KeepAwake;
    public string Status => _service.StatusText;

    public bool Enabled
    {
        get => Settings.Enabled;
        set { if (Settings.Enabled == value) return; Settings.Enabled = value; OnPropertyChanged(); _config.Touch(ConfigArea.KeepAwake); }
    }

    public void OnShown()
    {
        Days.Clear();
        string[] names = ["Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"];
        for (int i = 0; i < 7; i++) Days.Add(new DayToggleVM(Settings, _config, i + 1, names[i]));
        Overlaps.Clear();
        foreach (var t in _config.Current.Tools)
        {
            var preset = ToolPresets.Recognise(t);
            if (preset is null || !preset.HasOwnKeepAwake) continue;
            string text;
            if (preset.Key == "autopilot") text = "Keeps the PC awake only while a flow runs. No conflict.";
            else if (!t.Managed.Enabled || !t.Managed.SuppressKeepAwake) text = "Has its own keep-awake and DevPilot does not ask it to stop. Turn on \"Ask the tool to turn off its own keep-awake\" in Tools.";
            else if (!preset.SupportsSuppressEnv) text = "Has its own keep-awake. DevPilot asks it to stop (DEVPILOT_SUPPRESS=keepawake); it takes effect once the tool reads it. Until then it only holds a power request, which does not fight DevPilot.";
            else text = "Its own keep-awake is turned off while DevPilot manages it.";
            Overlaps.Add(new OverlapInfo(t.Name, text));
        }
        RefreshAll();
    }
}

// ════════════════════════════════════════════════════════════════════════
// Startup & conflicts
// ════════════════════════════════════════════════════════════════════════

public sealed class StartupEntryVM
{
    public required StartupEntry Entry { get; init; }
    public StartupConflict? Conflict { get; init; }
    public string Name => Entry.Name;
    public string Command => Entry.Command;
    public string Location => Entry.Location switch
    {
        StartupManager.LocationHkcu => "Registry (user)",
        StartupManager.LocationHklm => "Registry (machine)",
        _ => "Startup folder",
    };
    public bool IsManagedTool => Conflict is not null;
    public bool CanTakeOver => Conflict is not null && Entry.CanRemove;
    public string MatchText => Conflict is null ? string.Empty : $"{Conflict.Tool.Name} · {Conflict.Reason}";
    public required ICommand TakeOverCommand { get; init; }
}

public sealed class TakeoverVM
{
    public required StartupTakeover Model { get; init; }
    public required string ToolName { get; init; }
    public string Name => Model.Name;
    public string Detail => $"{ToolName} · taken over {Model.TakenAt:g}";
    public string Command => Model.Command;
    public required ICommand RestoreCommand { get; init; }
}

public sealed class StartupViewModel : ObservableObject, IPage
{
    private readonly IConfigStore _config;
    private readonly IStartupManager _startup;
    private bool _busy;

    public ObservableCollection<StartupEntryVM> Entries { get; } = [];
    public ObservableCollection<TakeoverVM> TakenOver { get; } = [];
    public ObservableCollection<ToolEntry> AutoStartTools { get; } = [];
    public ICommand RefreshCommand { get; }
    public ICommand TakeOverAllCommand { get; }

    public StartupViewModel(IConfigStore config, IStartupManager startup)
    {
        _config = config;
        _startup = startup;
        RefreshCommand = new RelayCommand(OnShown);
        TakeOverAllCommand = new RelayCommand(() =>
        {
            foreach (var e in Entries.Where(e => e.CanTakeOver).ToList()) TakeOver(e);
            OnShown();
        });
    }

    public StartupSettings Settings => _config.Current.Startup;
    public bool Busy { get => _busy; private set => Set(ref _busy, value); }
    public bool HasConflicts => Entries.Any(e => e.CanTakeOver);

    public bool StartWithWindows
    {
        get => Settings.StartWithWindows;
        set
        {
            if (Settings.StartWithWindows == value) return;
            try
            {
                _startup.SetDevPilotStartup(value);
                Settings.StartWithWindows = value;
                _config.Touch(ConfigArea.Startup);
            }
            catch (Exception ex) { Notifier.Error("Start with Windows", ex.Message); }
            OnPropertyChanged();
        }
    }

    public async void OnShown()
    {
        Busy = true;
        List<StartupEntry> entries;
        List<StartupConflict> conflicts;
        try
        {
            (entries, conflicts) = await Task.Run(() =>
            {
                var e = _startup.ScanEntries().ToList();
                var c = StartupMatcher.FindConflicts(e, _config.Current.Tools).ToList();
                return (e, c);
            });
        }
        catch (Exception ex)
        {
            Notifier.Error("Startup scan failed", ex.Message);
            Busy = false;
            return;
        }
        Entries.Clear();
        foreach (var e in entries.OrderByDescending(x => conflicts.Any(c => c.Entry == x)).ThenBy(x => x.Name))
        {
            var conflict = conflicts.FirstOrDefault(c => c.Entry == e);
            StartupEntryVM? vm = null;
            vm = new StartupEntryVM
            {
                Entry = e,
                Conflict = conflict,
                TakeOverCommand = new RelayCommand(() => { if (vm is not null) { TakeOver(vm); OnShown(); } }),
            };
            Entries.Add(vm);
        }
        TakenOver.Clear();
        foreach (var t in Settings.TakenOver.ToList())
        {
            var model = t;
            var tool = _config.Current.Tools.FirstOrDefault(x => x.Id == model.ToolId);
            TakenOver.Add(new TakeoverVM
            {
                Model = model,
                ToolName = tool?.Name ?? "(removed tool)",
                RestoreCommand = new RelayCommand(() =>
                {
                    try { _startup.Restore(model); }
                    catch (Exception ex) { Notifier.Error("Restore failed", ex.Message); }
                    OnShown();
                }),
            });
        }
        AutoStartTools.Clear();
        foreach (var t in _config.Current.Tools.Where(t => t.AutoStart.Enabled).OrderBy(t => t.AutoStart.Order))
            AutoStartTools.Add(t);
        Busy = false;
        OnPropertyChanged(nameof(HasConflicts));
        OnPropertyChanged(nameof(StartWithWindows));
    }

    private void TakeOver(StartupEntryVM e)
    {
        if (e.Conflict is null) return;
        try { _startup.TakeOver(e.Conflict); }
        catch (Exception ex) { Notifier.Error($"Could not take over {e.Name}", ex.Message); }
    }
}

// ════════════════════════════════════════════════════════════════════════
// Monitor
// ════════════════════════════════════════════════════════════════════════

public sealed class MonitorRowVM : ObservableObject
{
    private ToolStatus _status;
    public ToolEntry Tool { get; }
    public MonitorRowVM(ToolEntry tool, ToolStatus status) { Tool = tool; _status = status; }
    public string Name => Tool.Name;
    public string Glyph => FlyoutBuilder.GlyphOf(Tool);
    public ToolRunState State => _status.State;
    public string Detail => _status.State switch
    {
        ToolRunState.Running => "Running · " + _status.Detail,
        ToolRunState.Unknown => "Not checked yet",
        _ => _status.Detail,
    };
    public string Match => ProcessMatcher.CanTrack(Tool) ? string.Join("; ", ProcessMatcher.Patterns(Tool)) : Tool.Monitor.HealthUrl;
    public void Update(ToolStatus s) { _status = s; OnPropertyChanged(nameof(State)); OnPropertyChanged(nameof(Detail)); }
}

public sealed class HealthRowVM : ObservableObject
{
    private HealthStatus _health;
    public HealthTarget Model { get; }
    public HealthRowVM(HealthTarget model, HealthStatus health) { Model = model; _health = health; }
    public ToolRunState State => _health.Alive switch { true => ToolRunState.Running, false => ToolRunState.Stopped, _ => ToolRunState.Unknown };
    public string Detail => _health.Detail;
    public void Update(HealthStatus h) { _health = h; OnPropertyChanged(nameof(State)); OnPropertyChanged(nameof(Detail)); }
}

public sealed class MonitorViewModel : ObservableObject, IPage
{
    private readonly IConfigStore _config;
    private readonly IProcessMonitor _monitor;
    private readonly ILauncher _launcher;

    public ObservableCollection<MonitorRowVM> Rows { get; } = [];
    public ObservableCollection<HealthRowVM> Targets { get; } = [];
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand AddTargetCommand { get; }
    public ICommand RemoveTargetCommand { get; }
    public ICommand RefreshCommand { get; }

    public MonitorViewModel(IConfigStore config, IProcessMonitor monitor, ILauncher launcher)
    {
        _config = config;
        _monitor = monitor;
        _launcher = launcher;
        StartCommand = new AsyncCommand(async p => { if (p is MonitorRowVM r) await FlyoutBuilder.Run(_launcher.StartAsync(r.Tool), r.Name); });
        StopCommand = new AsyncCommand(async p => { if (p is MonitorRowVM r) await _launcher.StopAsync(r.Tool); });
        RestartCommand = new AsyncCommand(async p => { if (p is MonitorRowVM r) await FlyoutBuilder.Run(_launcher.RestartAsync(r.Tool), r.Name); });
        AddTargetCommand = new RelayCommand(() =>
        {
            var t = new HealthTarget { Name = "New check", Target = "http://127.0.0.1:8000/", IsUrl = true };
            _config.Current.Monitor.ExtraTargets.Add(t);
            _config.Touch(ConfigArea.Monitor);
            Targets.Add(new HealthRowVM(t, _monitor.GetHealth(t.Id)));
        });
        RemoveTargetCommand = new RelayCommand(p =>
        {
            if (p is not HealthRowVM r) return;
            _config.Current.Monitor.ExtraTargets.Remove(r.Model);
            Targets.Remove(r);
            _config.Touch(ConfigArea.Monitor);
        });
        RefreshCommand = new AsyncCommand(() => _monitor.RefreshNowAsync());
        _monitor.StatusChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(UpdateStatus);
    }

    public MonitorSettings Settings => _config.Current.Monitor;

    public void OnShown()
    {
        Rows.Clear();
        foreach (var t in _config.Current.Tools.Where(t => ProcessMatcher.CanTrack(t) || !string.IsNullOrWhiteSpace(t.Monitor.HealthUrl)))
            Rows.Add(new MonitorRowVM(t, _monitor.Get(t.Id)));
        Targets.Clear();
        foreach (var x in Settings.ExtraTargets) Targets.Add(new HealthRowVM(x, _monitor.GetHealth(x.Id)));
        OnPropertyChanged(nameof(Settings));
        _ = _monitor.RefreshNowAsync();
    }

    private void UpdateStatus()
    {
        foreach (var r in Rows) r.Update(_monitor.Get(r.Tool.Id));
        foreach (var t in Targets) t.Update(_monitor.GetHealth(t.Model.Id));
    }
}

// ════════════════════════════════════════════════════════════════════════
// Appearance
// ════════════════════════════════════════════════════════════════════════

public sealed class AppearanceViewModel : ObservableObject, IPage
{
    private readonly IConfigStore _config;
    private readonly IAppShell _shell;

    public static IReadOnlyList<string> AccentPalette { get; } =
        ["#4F8EF7", "#3B82F6", "#2BB3C0", "#14A38B", "#3FB28C", "#8FB339", "#D9A441", "#E07A3F", "#E5534B", "#E07AB5", "#A371F7", "#7C8591"];

    public static IReadOnlyList<Choice<HubAnchor>> Anchors { get; } =
    [
        new(HubAnchor.Auto, "Next to the tray icon"), new(HubAnchor.BottomRight, "Bottom right"), new(HubAnchor.BottomLeft, "Bottom left"),
        new(HubAnchor.TopRight, "Top right"), new(HubAnchor.TopLeft, "Top left"), new(HubAnchor.Center, "Centre of the screen"),
    ];
    public static IReadOnlyList<Choice<FlyoutSide>> FlyoutSides { get; } =
        [new(FlyoutSide.Auto, "Automatic"), new(FlyoutSide.Left, "Left"), new(FlyoutSide.Right, "Right")];
    public static IReadOnlyList<Choice<HubDensity>> Densities { get; } =
        [new(HubDensity.Compact, "Compact"), new(HubDensity.Comfortable, "Comfortable"), new(HubDensity.Spacious, "Spacious")];
    public static IReadOnlyList<Choice<GroupHeaderStyle>> HeaderStyles { get; } =
        [new(GroupHeaderStyle.SmallCaps, "SMALL CAPS"), new(GroupHeaderStyle.Normal, "Normal"), new(GroupHeaderStyle.Bold, "Bold"), new(GroupHeaderStyle.Hidden, "Hidden")];
    public static IReadOnlyList<Choice<HubLabelMode>> LabelModes => RowLook.HubLabelModes;
    public static IReadOnlyList<Choice<TrayClickAction>> ClickActions { get; } =
        [new(TrayClickAction.OpenHub, "Open the hub"), new(TrayClickAction.QuickMenu, "Quick menu"), new(TrayClickAction.OpenMainWindow, "Open DevPilot"), new(TrayClickAction.Nothing, "Nothing")];

    public ICommand SetAccentCommand { get; }
    public ICommand SetSpotlightAccentCommand { get; }
    public ICommand PreviewCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand BrowseCapturesCommand { get; }

    public AppearanceViewModel(IConfigStore config, IAppShell shell)
    {
        _config = config;
        _shell = shell;
        SetAccentCommand = new RelayCommand(p => { S.Accent = ConfigMigrator.NormalizeColor(p as string) is { Length: > 0 } c ? c : S.Accent; _config.Touch(ConfigArea.Theme); OnPropertyChanged(nameof(Accent)); });
        SetSpotlightAccentCommand = new RelayCommand(p => { Hub.Spotlight.Accent = ConfigMigrator.NormalizeColor(p as string); _config.Touch(ConfigArea.Theme); OnPropertyChanged(nameof(SpotlightAccent)); });
        PreviewCommand = new RelayCommand(() => _shell.ShowHub(pinned: true));
        ResetCommand = new RelayCommand(() =>
        {
            if (MessageBox.Show("Reset the hub appearance to the defaults? Tools and groups are not touched.", "DevPilot", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
            S.Hub = new HubAppearance();
            S.Accent = "#4F8EF7";
            _config.Touch(ConfigArea.Hub);
            _config.Touch(ConfigArea.Theme);
            using (EditTracker.Suppress()) RefreshAll();
        });
        BrowseCapturesCommand = new RelayCommand(() =>
        {
            var dlg = new OpenFolderDialog { Title = "ScreenPilot captures folder" };
            if (dlg.ShowDialog() != true) return;
            Hub.Spotlight.CapturesFolder = dlg.FolderName;
            _config.Touch(ConfigArea.Hub);
            OnPropertyChanged(nameof(Hub));
        });
        _config.Changed += (_, a) => { if (a == ConfigArea.All) Application.Current.Dispatcher.BeginInvoke(() => { using (EditTracker.Suppress()) RefreshAll(); }); };
    }

    private AppSettings S => _config.Current;
    public AppSettings Settings => S;
    public HubAppearance Hub => S.Hub;

    public string Accent
    {
        get => S.Accent;
        set { var c = ConfigMigrator.NormalizeColor(value); if (c.Length == 0 || c == S.Accent) return; S.Accent = c; _config.Touch(ConfigArea.Theme); OnPropertyChanged(); }
    }

    public string SpotlightAccent
    {
        get => Hub.Spotlight.Accent;
        set { Hub.Spotlight.Accent = ConfigMigrator.NormalizeColor(value); _config.Touch(ConfigArea.Theme); OnPropertyChanged(); }
    }

    public ThemeMode Theme
    {
        get => S.Theme;
        set { if (S.Theme == value) return; S.Theme = value; _config.Touch(ConfigArea.Theme); OnPropertyChanged(); }
    }

    public void OnShown()
    {
        using (EditTracker.Suppress()) RefreshAll();
    }
}

// ════════════════════════════════════════════════════════════════════════
// Settings (general, hotkeys, backup)
// ════════════════════════════════════════════════════════════════════════

public sealed class HotkeyRowVM : ObservableObject
{
    private readonly IConfigStore _config;
    private bool _failed;
    public HotkeyConfig Model { get; }
    public IReadOnlyList<Choice<string>> Actions { get; }

    public HotkeyRowVM(HotkeyConfig model, IReadOnlyList<Choice<string>> actions, IConfigStore config, bool failed)
    {
        Model = model; Actions = actions; _config = config; _failed = failed;
    }

    public static IReadOnlyList<string> Keys { get; } = BuildKeys();
    private static IReadOnlyList<string> BuildKeys()
    {
        var list = new List<string>();
        for (char c = 'A'; c <= 'Z'; c++) list.Add(c.ToString());
        for (char c = '0'; c <= '9'; c++) list.Add(c.ToString());
        for (int i = 1; i <= 12; i++) list.Add("F" + i);
        list.AddRange(["Space", "Enter", "Tab", "Insert", "Delete", "Home", "End", "PageUp", "PageDown", "Left", "Up", "Right", "Down", "PrintScreen", "Pause"]);
        return list;
    }

    private bool Has(string m) => Model.Modifiers.Split('+', StringSplitOptions.TrimEntries).Contains(m, StringComparer.OrdinalIgnoreCase);
    private void SetMod(string m, bool on)
    {
        var parts = new[] { "Ctrl", "Alt", "Shift", "Win" }.Where(x => x == m ? on : Has(x));
        Model.Modifiers = string.Join("+", parts);
        OnPropertyChanged(nameof(Display));
        _config.Touch(ConfigArea.Hotkeys);
    }

    public bool Ctrl { get => Has("Ctrl"); set => SetMod("Ctrl", value); }
    public bool Alt { get => Has("Alt"); set => SetMod("Alt", value); }
    public bool Shift { get => Has("Shift"); set => SetMod("Shift", value); }
    public bool Win { get => Has("Win"); set => SetMod("Win", value); }
    public string Display => string.IsNullOrWhiteSpace(Model.Modifiers) ? Model.Key : $"{Model.Modifiers}+{Model.Key}";
    public bool Failed { get => _failed; set => Set(ref _failed, value); }
}

public sealed class SettingsViewModel : ObservableObject, IPage
{
    private readonly IConfigStore _config;
    private readonly IHotkeyManager _hotkeys;
    public ObservableCollection<HotkeyRowVM> Hotkeys { get; } = [];
    public ICommand AddHotkeyCommand { get; }
    public ICommand RemoveHotkeyCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenLogCommand { get; }
    public ICommand ResetCommand { get; }

    /// <summary>Hotkeys that could not be registered (set by the app after registration).</summary>
    public static HashSet<int> FailedHotkeys { get; } = [];

    public SettingsViewModel(IConfigStore config, IHotkeyManager hotkeys)
    {
        _config = config;
        _hotkeys = hotkeys;
        AddHotkeyCommand = new RelayCommand(() =>
        {
            var hk = new HotkeyConfig { Id = (_config.Current.Hotkeys.Count == 0 ? 0 : _config.Current.Hotkeys.Max(h => h.Id)) + 1, Action = "hub", Modifiers = "Ctrl+Alt", Key = "H" };
            _config.Current.Hotkeys.Add(hk);
            _config.Touch(ConfigArea.Hotkeys);
            OnShown();
        });
        RemoveHotkeyCommand = new RelayCommand(p =>
        {
            if (p is not HotkeyRowVM r) return;
            _config.Current.Hotkeys.Remove(r.Model);
            _config.Touch(ConfigArea.Hotkeys);
            OnShown();
        });
        ExportCommand = new RelayCommand(() =>
        {
            var dlg = new SaveFileDialog { FileName = $"devpilot-config-{DateTime.Now:yyyyMMdd}.json", Filter = "JSON (*.json)|*.json" };
            if (dlg.ShowDialog() != true) return;
            _config.Export(dlg.FileName);
            Notifier.Info("Exported", dlg.FileName);
        });
        ImportCommand = new RelayCommand(() =>
        {
            var dlg = new OpenFileDialog { Filter = "JSON (*.json)|*.json" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                _config.Import(dlg.FileName);
                Notifier.Info("Imported", "The previous configuration was kept as a backup in the data folder.");
            }
            catch (Exception ex) { MessageBox.Show("Import failed: " + ex.Message, "DevPilot"); }
            OnShown();
        });
        OpenDataFolderCommand = new RelayCommand(() => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_config.DataFolder}\"") { UseShellExecute = true }));
        OpenLogCommand = new RelayCommand(() =>
        {
            var log = App.LogFile;
            if (File.Exists(log)) Process.Start(new ProcessStartInfo(log) { UseShellExecute = true });
        });
        ResetCommand = new RelayCommand(() =>
        {
            if (MessageBox.Show("Reset everything to the defaults? A backup of the current configuration is kept in the data folder.", "DevPilot",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            _config.Reset();
            OnShown();
        });
        _config.Changed += (_, a) => { if (a == ConfigArea.All) Application.Current.Dispatcher.BeginInvoke(OnShown); };
    }

    public AppSettings Settings => _config.Current;
    public string ConfigPath => _config.ConfigPath;
    public string LogPath => App.LogFile;

    public IReadOnlyList<Choice<string>> ActionChoices()
    {
        var list = new List<Choice<string>>
        {
            new("hub", "Open / close the hub"),
            new("main", "Open DevPilot"),
            new("keepawake", "Turn keep-awake on / off"),
        };
        foreach (var t in _config.Current.Tools.Where(t => !t.IsSeparator).OrderBy(t => t.Name))
        {
            list.Add(new($"tool:{t.Id}", $"Start {t.Name}"));
            foreach (var a in t.Actions.Where(a => a.Kind != ActionKind.Separator))
                list.Add(new($"action:{t.Id}:{a.Id}", $"{t.Name} — {a.Label}"));
        }
        return list;
    }

    public void OnShown()
    {
        using (EditTracker.Suppress())
        {
            Hotkeys.Clear();
            var choices = ActionChoices();
            foreach (var h in _config.Current.Hotkeys) Hotkeys.Add(new HotkeyRowVM(h, choices, _config, FailedHotkeys.Contains(h.Id)));
            RefreshAll();
        }
    }

    public void MarkFailed(IEnumerable<HotkeyConfig> failed)
    {
        FailedHotkeys.Clear();
        foreach (var f in failed) FailedHotkeys.Add(f.Id);
        foreach (var r in Hotkeys) r.Failed = FailedHotkeys.Contains(r.Model.Id);
    }
}

public sealed class GuideViewModel : ObservableObject, IPage
{
    public void OnShown() { }
}
