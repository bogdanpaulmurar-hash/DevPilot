// ============================================================
// DevPilot — IT Automation Hub v1.0
// SettingsViewModel.cs — ViewModel for Settings tab
// Phase: 13 (+HideJigglerInTray, +TrayTypeConfigs, +RestartCommand,
//             tray limit raised to 20, type-badge colours on TrayEntries)
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using DevPilot.Core.Interfaces;
using DevPilot.Models;
using DevPilot.UI.Commands;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DevPilot.UI.ViewModels;

/// <summary>ViewModel for the Settings tab. Every control is fully wired.</summary>
public sealed class SettingsViewModel : BaseViewModel
{
    // ── Dependencies ──────────────────────────────────────────────────────
    private readonly ISettingsManager           _settings;
    private readonly IStartupService            _startup;
    private readonly ITrayService               _trayService;
    private readonly ILogger<SettingsViewModel> _logger;

    // ── Dialog factory — injected from MainWindow.xaml.cs ────────────────
    /// <summary>
    /// Opens the predefined hotkey picker.
    /// Receives (displayName, currentModifiers, currentKey).
    /// Returns (Modifiers, Key) or null if cancelled.
    /// </summary>
    public Func<string, string, string, Task<(string Modifiers, string Key)?>>? ShowHotkeyPickerDialog { get; set; }

    /// <summary>Raised after Save() so MainWindow can re-register hotkeys immediately.</summary>
    public event EventHandler? HotkeysChanged;

    // ══════════════════════════════════════════════════════════════════════
    // SECTION: General
    // ══════════════════════════════════════════════════════════════════════

    private bool _isDarkTheme;
    /// <summary>Toggles theme; applies immediately via ThemeManager.</summary>
    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set
        {
            if (SetProperty(ref _isDarkTheme, value))
            {
                OnPropertyChanged(nameof(IsLightTheme));
                _settings.Current.Theme = value ? "dark" : "light";
                _settings.Save();
                ThemeManager.Apply(value);
            }
        }
    }

    /// <summary>Inverse of IsDarkTheme — for RadioButton binding.</summary>
    public bool IsLightTheme
    {
        get => !_isDarkTheme;
        set => IsDarkTheme = !value;
    }

    private bool _startWithWindows;
    /// <summary>Writes/removes the startup registry entry immediately when toggled.</summary>
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (SetProperty(ref _startWithWindows, value))
            {
                try   { _startup.SetStartup(value); }
                catch (Exception ex) { _logger.LogError(ex, "SetStartup failed."); }
            }
        }
    }

    private bool _minimizeToTray;
    /// <summary>Persisted on Save; controls X button behaviour.</summary>
    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => SetProperty(ref _minimizeToTray, value);
    }

    // ══════════════════════════════════════════════════════════════════════
    // SECTION: Tab Visibility
    // ══════════════════════════════════════════════════════════════════════

    private bool _isToolsTabVisible;
    public bool IsToolsTabVisible
    {
        get => _isToolsTabVisible;
        set { SetProperty(ref _isToolsTabVisible, value); OnPropertyChanged(nameof(ToolsTabVisibility)); }
    }

    private bool _isJigglerTabVisible;
    public bool IsJigglerTabVisible
    {
        get => _isJigglerTabVisible;
        set { SetProperty(ref _isJigglerTabVisible, value); OnPropertyChanged(nameof(JigglerTabVisibility)); }
    }

    private bool _isScreenshotTabVisible;
    public bool IsScreenshotTabVisible
    {
        get => _isScreenshotTabVisible;
        set { SetProperty(ref _isScreenshotTabVisible, value); OnPropertyChanged(nameof(ScreenshotTabVisibility)); }
    }

    private bool _isHeartbeatTabVisible;
    public bool IsHeartbeatTabVisible
    {
        get => _isHeartbeatTabVisible;
        set { SetProperty(ref _isHeartbeatTabVisible, value); OnPropertyChanged(nameof(HeartbeatTabVisibility)); }
    }

    // Visibility helpers — bound directly to TabItem.Visibility in MainWindow.xaml
    public Visibility ToolsTabVisibility      => _isToolsTabVisible      ? Visibility.Visible : Visibility.Collapsed;
    public Visibility JigglerTabVisibility    => _isJigglerTabVisible    ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ScreenshotTabVisibility => _isScreenshotTabVisible ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HeartbeatTabVisibility  => _isHeartbeatTabVisible  ? Visibility.Visible : Visibility.Collapsed;

    // ══════════════════════════════════════════════════════════════════════
    // SECTION: Paths
    // ══════════════════════════════════════════════════════════════════════

    private string _dataCleansingExePath = string.Empty;
    public string DataCleansingExePath
    {
        get => _dataCleansingExePath;
        set => SetProperty(ref _dataCleansingExePath, value);
    }

    private string _testClientExePath = string.Empty;
    public string TestClientExePath
    {
        get => _testClientExePath;
        set => SetProperty(ref _testClientExePath, value);
    }

    private string _tcMgmtStarterExePath = string.Empty;
    public string TcMgmtStarterExePath
    {
        get => _tcMgmtStarterExePath;
        set => SetProperty(ref _tcMgmtStarterExePath, value);
    }

    private string _screenshotSaveFolder = string.Empty;
    public string ScreenshotSaveFolder
    {
        get => _screenshotSaveFolder;
        set => SetProperty(ref _screenshotSaveFolder, value);
    }

    public ICommand BrowseDataCleansingCommand    { get; }
    public ICommand BrowseTestClientCommand       { get; }
    public ICommand BrowseTcMgmtStarterCommand    { get; }
    public ICommand BrowseScreenshotFolderCommand { get; }
    public ICommand OpenFolderDataCleansingCommand  { get; }
    public ICommand OpenFolderTestClientCommand     { get; }
    public ICommand OpenFolderTcMgmtStarterCommand  { get; }
    public ICommand OpenFolderScreenshotCommand     { get; }

    // ══════════════════════════════════════════════════════════════════════
    // SECTION: Hotkeys
    // ══════════════════════════════════════════════════════════════════════

    public ObservableCollection<HotkeyRowViewModel> Hotkeys { get; } = [];

    // ══════════════════════════════════════════════════════════════════════
    // SECTION: Mouse Jiggler
    // ══════════════════════════════════════════════════════════════════════

    private int _jigglerIntervalSeconds;
    /// <summary>Interval 10–300 s.</summary>
    public int JigglerIntervalSeconds
    {
        get => _jigglerIntervalSeconds;
        set => SetProperty(ref _jigglerIntervalSeconds, Math.Clamp(value, 10, 300));
    }

    public IReadOnlyList<string> JigglerMethods { get; } =
        [AppConstants.JigglerMethodMouseMove, AppConstants.JigglerMethodKeyPress];

    private string _jigglerMethod = AppConstants.JigglerMethodMouseMove;
    public string JigglerMethod
    {
        get => _jigglerMethod;
        set => SetProperty(ref _jigglerMethod, value);
    }

    private bool _hideJigglerInTray;
    /// <summary>
    /// When true, the Jiggler toggle item is removed from the tray context menu.
    /// The Jiggler service continues to run normally.
    /// </summary>
    public bool HideJigglerInTray
    {
        get => _hideJigglerInTray;
        set => SetProperty(ref _hideJigglerInTray, value);
    }

    private int _traySidebarWidth = 26;
    /// <summary>Width in pixels of the teal sidebar in the tray context menu (16–60).</summary>
    public int TraySidebarWidth
    {
        get => _traySidebarWidth;
        set
        {
            if (_traySidebarWidth == value) return;
            _traySidebarWidth = Math.Clamp(value, 16, 60);
            OnPropertyChanged();
            _settings.Current.TraySidebarWidth = _traySidebarWidth;
            _settings.Save();
            _trayService.RebuildMenu(); // preview live instant
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // SECTION: Heartbeat
    // ══════════════════════════════════════════════════════════════════════

    private int _heartbeatIntervalSeconds;
    public int HeartbeatIntervalSeconds
    {
        get => _heartbeatIntervalSeconds;
        set => SetProperty(ref _heartbeatIntervalSeconds, Math.Max(5, value));
    }

    public ObservableCollection<HeartbeatTarget> HeartbeatTargets { get; } = [];

    private string _newTargetName = string.Empty;
    public string NewTargetName
    {
        get => _newTargetName;
        set
        {
            SetProperty(ref _newTargetName, value);
            ((RelayCommand)AddHeartbeatTargetCommand).RaiseCanExecuteChanged();
        }
    }

    private string _newTargetTarget = string.Empty;
    public string NewTargetTarget
    {
        get => _newTargetTarget;
        set
        {
            SetProperty(ref _newTargetTarget, value);
            ((RelayCommand)AddHeartbeatTargetCommand).RaiseCanExecuteChanged();
        }
    }

    private HeartbeatTargetType _newTargetType = HeartbeatTargetType.Process;
    public HeartbeatTargetType NewTargetType
    {
        get => _newTargetType;
        set => SetProperty(ref _newTargetType, value);
    }

    public IReadOnlyList<HeartbeatTargetType> TargetTypes { get; } =
        Enum.GetValues<HeartbeatTargetType>().ToArray();

    public ICommand AddHeartbeatTargetCommand    { get; }
    public ICommand RemoveHeartbeatTargetCommand { get; }

    // ══════════════════════════════════════════════════════════════════════
    // SECTION: Tray Customizer
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>One row per App Launcher entry, with ShowInTray toggle and type badge.</summary>
    public ObservableCollection<TrayAppEntryViewModel> TrayEntries { get; } = [];

    /// <summary>One row per AppEntryType — editable emoji + color.</summary>
    public ObservableCollection<TrayTypeConfigViewModel> TrayTypeConfigs { get; } = [];

    /// <summary>Warning shown when more than 20 entries are checked.</summary>
    public string TrayLimitWarning => TrayEntries.Count(e => e.ShowInTray) > 20
        ? "⚠  Maximum 20 shortcuts allowed in tray menu."
        : string.Empty;

    public Visibility TrayLimitVisibility =>
        string.IsNullOrEmpty(TrayLimitWarning) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility EmptyAppsVisibility =>
        TrayEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    // ══════════════════════════════════════════════════════════════════════
    // SECTION: Save / Reset / Restart + Status
    // ══════════════════════════════════════════════════════════════════════

    private string _statusText = string.Empty;
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public ICommand SaveCommand            { get; }
    public ICommand ResetToDefaultsCommand { get; }
    /// <summary>Saves settings then restarts DevPilot.exe cleanly.</summary>
    public ICommand RestartCommand         { get; }

    // ══════════════════════════════════════════════════════════════════════
    // CONSTRUCTOR
    // ══════════════════════════════════════════════════════════════════════

    public SettingsViewModel(
        ISettingsManager           settings,
        IStartupService            startup,
        ITrayService               trayService,
        ILogger<SettingsViewModel> logger)
    {
        _settings    = settings;
        _startup     = startup;
        _trayService = trayService;
        _logger      = logger;

        // Paths — Browse
        BrowseDataCleansingCommand    = new RelayCommand(_ => BrowseExe(v => DataCleansingExePath  = v));
        BrowseTestClientCommand       = new RelayCommand(_ => BrowseExe(v => TestClientExePath     = v));
        BrowseTcMgmtStarterCommand    = new RelayCommand(_ => BrowseExe(v => TcMgmtStarterExePath  = v));
        BrowseScreenshotFolderCommand = new RelayCommand(_ => BrowseFolder(v => ScreenshotSaveFolder = v));

        // Paths — Open Folder
        OpenFolderDataCleansingCommand  = new RelayCommand(
            _ => OpenFolder(DataCleansingExePath),
            _ => !string.IsNullOrWhiteSpace(DataCleansingExePath));
        OpenFolderTestClientCommand     = new RelayCommand(
            _ => OpenFolder(TestClientExePath),
            _ => !string.IsNullOrWhiteSpace(TestClientExePath));
        OpenFolderTcMgmtStarterCommand  = new RelayCommand(
            _ => OpenFolder(TcMgmtStarterExePath),
            _ => !string.IsNullOrWhiteSpace(TcMgmtStarterExePath));
        OpenFolderScreenshotCommand     = new RelayCommand(
            _ => OpenFolder(ScreenshotSaveFolder, isDirectory: true),
            _ => !string.IsNullOrWhiteSpace(ScreenshotSaveFolder));

        // Heartbeat
        AddHeartbeatTargetCommand = new RelayCommand(
            _ => AddHeartbeatTarget(),
            _ => !string.IsNullOrWhiteSpace(NewTargetName) && !string.IsNullOrWhiteSpace(NewTargetTarget));
        RemoveHeartbeatTargetCommand = new RelayCommand(
            p => RemoveHeartbeatTarget(p as HeartbeatTarget));

        // Save / Reset / Restart
        SaveCommand            = new RelayCommand(_ => Save());
        ResetToDefaultsCommand = new RelayCommand(_ => ResetToDefaults());
        RestartCommand         = new RelayCommand(_ => RestartApp());

        LoadFromSettings();
    }

    // ══════════════════════════════════════════════════════════════════════
    // PUBLIC: Reload (also called after Reset)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Re-populates all VM state from the current AppSettings snapshot.</summary>
    public void LoadFromSettings()
    {
        var s = _settings.Current;

        // General
        _isDarkTheme      = s.Theme == "dark";
        _startWithWindows = _startup.IsStartupEnabled;
        _minimizeToTray   = s.MinimizeToTray;

        // Tab Visibility
        _isToolsTabVisible      = s.TabVisibility.GetValueOrDefault(AppConstants.TabTools,      true);
        _isJigglerTabVisible    = s.TabVisibility.GetValueOrDefault(AppConstants.TabJiggler,    true);
        _isScreenshotTabVisible = s.TabVisibility.GetValueOrDefault(AppConstants.TabScreenshot, true);
        _isHeartbeatTabVisible  = s.TabVisibility.GetValueOrDefault(AppConstants.TabHeartbeat,  true);

        // Paths
        _dataCleansingExePath  = s.DataCleansingExePath;
        _testClientExePath     = s.TestClientExePath;
        _tcMgmtStarterExePath  = s.TcMgmtStarterExePath;
        _screenshotSaveFolder  = s.ScreenshotSaveFolder;

        // Jiggler
        _jigglerIntervalSeconds = Math.Clamp(s.JigglerIntervalSec, 10, 300);
        _jigglerMethod          = string.IsNullOrWhiteSpace(s.JigglerMethod)
                                  ? AppConstants.JigglerMethodMouseMove
                                  : s.JigglerMethod;
        _hideJigglerInTray      = s.HideJigglerInTray;
        _traySidebarWidth        = Math.Clamp(s.TraySidebarWidth, 16, 60);

        // Heartbeat
        _heartbeatIntervalSeconds = Math.Max(5, s.HeartbeatIntervalSec);
        HeartbeatTargets.Clear();
        foreach (var t in s.HeartbeatTargets)
            HeartbeatTargets.Add(t);

        // Hotkeys — rebuild rows from persisted config (fallback to factory defaults)
        Hotkeys.Clear();
        var hotkeyDefs = new (int Id, string Action, string Name, string Mods, string Key)[]
        {
            (1, AppConstants.HotkeyActionShowHide,      "Show / Hide DevPilot", "Ctrl+Shift", "D"),
            (2, AppConstants.HotkeyActionToggleJiggler, "Toggle Jiggler",        "Ctrl+Shift", "J"),
            (3, AppConstants.HotkeyActionScreenshot,    "Take Screenshot",       "Ctrl+Shift", "S"),
        };
        foreach (var def in hotkeyDefs)
        {
            var saved = s.Hotkeys.FirstOrDefault(h => h.Id == def.Id);
            var row = new HotkeyRowViewModel(
                def.Id, def.Action, def.Name,
                saved?.Modifiers ?? def.Mods,
                saved?.Key       ?? def.Key,
                saved?.IsEnabled ?? true,
                def.Mods, def.Key);
            row.OnChangeRequested = async () =>
                await (ShowHotkeyPickerDialog?.Invoke(row.DisplayName, row.Modifiers, row.Key)
                       ?? Task.FromResult<(string Modifiers, string Key)?>(null));
            Hotkeys.Add(row);
        }

        // ── Tray Type Configs ─────────────────────────────────────────────
        // Build fallback dictionary; if config.json has no entry for a type, use preset
        var defaultTypeConfigs = TrayTypeDefaults.Build();
        TrayTypeConfigs.Clear();
        foreach (var typeName in Enum.GetNames<AppEntryType>())
        {
            if (!s.TrayTypeConfigs.TryGetValue(typeName, out var cfg) || cfg is null)
                cfg = defaultTypeConfigs.GetValueOrDefault(typeName) ?? new TrayTypeConfig();
            TrayTypeConfigs.Add(new TrayTypeConfigViewModel(typeName, cfg));
        }

        // ── Tray Customizer ───────────────────────────────────────────────
        // Source of truth: AppEntry.ShowInTray (persisted per-entry in "apps" array)
        TrayEntries.Clear();
        foreach (var app in s.Apps)
        {
            var typeName = app.Type.ToString();
            s.TrayTypeConfigs.TryGetValue(typeName, out var typeCfg);
            typeCfg ??= defaultTypeConfigs.GetValueOrDefault(typeName) ?? new TrayTypeConfig();

            var vm = new TrayAppEntryViewModel(
                app.Id, app.Name, app.Type, app.ShowInTray,
                typeCfg.Emoji, typeCfg.Color,
                app.TagEmoji, app.TagColor,
                app.TrayFontStyle);

            vm.PropertyChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(TrayLimitWarning));
                OnPropertyChanged(nameof(TrayLimitVisibility));
            };
            TrayEntries.Add(vm);
        }

        // Raise all change notifications for initial binding
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(StartWithWindows));
        OnPropertyChanged(nameof(MinimizeToTray));
        OnPropertyChanged(nameof(IsToolsTabVisible));
        OnPropertyChanged(nameof(IsJigglerTabVisible));
        OnPropertyChanged(nameof(IsScreenshotTabVisible));
        OnPropertyChanged(nameof(IsHeartbeatTabVisible));
        OnPropertyChanged(nameof(ToolsTabVisibility));
        OnPropertyChanged(nameof(JigglerTabVisibility));
        OnPropertyChanged(nameof(ScreenshotTabVisibility));
        OnPropertyChanged(nameof(HeartbeatTabVisibility));
        OnPropertyChanged(nameof(DataCleansingExePath));
        OnPropertyChanged(nameof(TestClientExePath));
        OnPropertyChanged(nameof(TcMgmtStarterExePath));
        OnPropertyChanged(nameof(ScreenshotSaveFolder));
        OnPropertyChanged(nameof(JigglerIntervalSeconds));
        OnPropertyChanged(nameof(JigglerMethod));
        OnPropertyChanged(nameof(HideJigglerInTray));
        OnPropertyChanged(nameof(TraySidebarWidth));
        OnPropertyChanged(nameof(HeartbeatIntervalSeconds));
        OnPropertyChanged(nameof(TrayLimitWarning));
        OnPropertyChanged(nameof(TrayLimitVisibility));
        OnPropertyChanged(nameof(EmptyAppsVisibility));
    }

    // ══════════════════════════════════════════════════════════════════════
    // PRIVATE: Save
    // ══════════════════════════════════════════════════════════════════════

    private void Save()
    {
        var s = _settings.Current;

        // General
        s.Theme          = _isDarkTheme ? "dark" : "light";
        s.MinimizeToTray = _minimizeToTray;

        // Tab Visibility
        s.TabVisibility[AppConstants.TabTools]      = _isToolsTabVisible;
        s.TabVisibility[AppConstants.TabJiggler]    = _isJigglerTabVisible;
        s.TabVisibility[AppConstants.TabScreenshot] = _isScreenshotTabVisible;
        s.TabVisibility[AppConstants.TabHeartbeat]  = _isHeartbeatTabVisible;

        // Paths
        s.DataCleansingExePath = _dataCleansingExePath;
        s.TestClientExePath    = _testClientExePath;
        s.TcMgmtStarterExePath = _tcMgmtStarterExePath;
        s.ScreenshotSaveFolder = _screenshotSaveFolder;

        // Jiggler
        s.JigglerIntervalSec  = _jigglerIntervalSeconds;
        s.JigglerMethod       = _jigglerMethod;
        s.HideJigglerInTray   = _hideJigglerInTray;
        s.TraySidebarWidth    = _traySidebarWidth;

        // Heartbeat
        s.HeartbeatIntervalSec = _heartbeatIntervalSeconds;
        s.HeartbeatTargets     = HeartbeatTargets.ToList();

        // Tray Type Configs — persist editable emoji/color rows
        s.TrayTypeConfigs = TrayTypeConfigs.ToDictionary(vm => vm.TypeKey, vm => vm.ToModel());

        // Tray Customizer — persist ShowInTray back onto each AppEntry
        foreach (var vm in TrayEntries)
        {
            var app = s.Apps.FirstOrDefault(a => a.Id == vm.AppId);
            if (app is not null)
            {
                app.ShowInTray = vm.ShowInTray;
                app.TagEmoji   = vm.TagEmoji;
                app.TagColor   = vm.TagColor;
                app.TrayFontStyle = vm.TrayFontStyle;
            }
        }
        // Legacy dictionary compat
        s.ShowInTray = TrayEntries.ToDictionary(e => e.AppId, e => e.ShowInTray);

        // Refresh badge colours on existing TrayEntry rows immediately
        foreach (var entry in TrayEntries)
        {
            if (s.TrayTypeConfigs.TryGetValue(entry.TypeLabel, out var cfg))
            {
                entry.TypeEmoji = cfg.Emoji;
                entry.TypeColor = cfg.Color;
                // TagEmoji/TagColor are user-owned; don't reset them here
            }
        }

        // Hotkeys
        s.Hotkeys = Hotkeys.Select(r => new HotkeyConfig
        {
            Id        = r.Id,
            Action    = r.ActionKey,
            Modifiers = r.Modifiers,
            Key       = r.Key,
            IsEnabled = r.IsEnabled,
        }).ToList();

        _settings.Save();

        // Rebuild tray menu immediately — no restart required
        _trayService.RebuildMenu();

        StatusText = "✔  Settings saved successfully.";
        _logger.LogInformation("Settings saved.");

        HotkeysChanged?.Invoke(this, EventArgs.Empty);
    }

    // ══════════════════════════════════════════════════════════════════════
    // PRIVATE: Reset to Defaults
    // ══════════════════════════════════════════════════════════════════════

    private void ResetToDefaults()
    {
        var result = MessageBox.Show(
            "Reset all settings to defaults?\nThis cannot be undone.",
            "Reset to Defaults",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        _settings.Reset();
        LoadFromSettings();
        ThemeManager.Apply(_isDarkTheme);

        StatusText = "✔  Settings reset to defaults.";
        _logger.LogInformation("Settings reset to defaults.");
    }

    // ══════════════════════════════════════════════════════════════════════
    // PRIVATE: Restart App
    // ══════════════════════════════════════════════════════════════════════

    private void RestartApp()
    {
        Save(); // persist before restarting

        var exe = Environment.ProcessPath
               ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;

        if (string.IsNullOrEmpty(exe))
        {
            _logger.LogWarning("RestartApp: could not determine process path.");
            return;
        }

        // Release the single-instance mutex BEFORE starting the new process,
        // otherwise the new instance sees the mutex locked and shuts itself down immediately.
        try
        {
            var mtx = new System.Threading.Mutex(false, App.MutexName);
            mtx.Dispose(); // just opening it; actual release happens in App.OnExit
        }
        catch { /* best effort */ }

        var psi = new System.Diagnostics.ProcessStartInfo(exe)
        {
            UseShellExecute = true
        };
        System.Diagnostics.Process.Start(psi);

        // Shutdown current instance on the UI thread
        try
        {
            Application.Current.Dispatcher.BeginInvoke(() => Application.Current.Shutdown(0));
        }
        catch
        {
            Environment.Exit(0);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // PRIVATE: Helpers
    // ══════════════════════════════════════════════════════════════════════

    private static void BrowseExe(Action<string> setter)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select executable",
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
            setter(dlg.FileName);
    }

    private static void BrowseFolder(Action<string> setter)
    {
        var dlg = new OpenFolderDialog { Title = "Select folder" };
        if (dlg.ShowDialog() == true)
            setter(dlg.FolderName);
    }

    private static void OpenFolder(string path, bool isDirectory = false)
    {
        var dir = isDirectory ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;
        System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
    }

    private void AddHeartbeatTarget()
    {
        HeartbeatTargets.Add(new HeartbeatTarget
        {
            Id        = Guid.NewGuid().ToString("N")[..8],
            Name      = NewTargetName.Trim(),
            Target    = NewTargetTarget.Trim(),
            Type      = NewTargetType,
            IsEnabled = true,
        });
        NewTargetName   = string.Empty;
        NewTargetTarget = string.Empty;
    }

    private void RemoveHeartbeatTarget(HeartbeatTarget? target)
    {
        if (target is not null)
            HeartbeatTargets.Remove(target);
    }
}