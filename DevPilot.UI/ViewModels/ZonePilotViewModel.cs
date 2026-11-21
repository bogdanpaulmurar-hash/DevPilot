using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using DevPilot.Core.Integrations;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Launch;
using DevPilot.Core.Presets;
using DevPilot.Models;
using DevPilot.UI.Mvvm;

namespace DevPilot.UI.ViewModels;

/// <summary>One monitor on the ZonePilot page: its own layout, or "" for the default layout.</summary>
public sealed class ZonePilotMonitorVM : ObservableObject
{
    private string _layoutId;

    public ZonePilotMonitorVM(ZonePilotMonitor monitor, string layoutId)
    {
        Monitor = monitor;
        _layoutId = layoutId;
    }

    public ZonePilotMonitor Monitor { get; }
    public string Title => Monitor.Describe();
    public string LayoutId { get => _layoutId; set => Set(ref _layoutId, value ?? string.Empty); }
}

/// <summary>One hotkey (or the modifiers for 1..9) with its validation message.</summary>
public sealed class ZonePilotHotkeyVM : ObservableObject
{
    private string _text;
    private string _error = string.Empty;

    public ZonePilotHotkeyVM(string key, string label, string text, bool modifiersOnly = false)
    {
        Key = key;
        Label = label;
        ModifiersOnly = modifiersOnly;
        _text = text;
        Validate();
    }

    public string Key { get; }
    public string Label { get; }
    public bool ModifiersOnly { get; }

    public string Text
    {
        get => _text;
        set { if (Set(ref _text, value ?? string.Empty)) Validate(); }
    }

    public string Error { get => _error; private set { if (Set(ref _error, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => Error.Length > 0;

    public bool Validate()
    {
        string error;
        bool ok = ModifiersOnly ? ZonePilotSettings.IsValidModifiers(Text, out error) : ZonePilotSettings.IsValidHotkey(Text, out error);
        Error = ok ? string.Empty : error;
        return ok;
    }
}

/// <summary>
/// "ZonePilot" page (opened by the tool's <c>Configure…</c> option): edits <c>%APPDATA%\ZonePilot\settings.json</c>
/// (default layout, drag key, hotkeys), sets per-monitor layouts with <c>ZonePilot apply &lt;layout&gt; --monitor &lt;n&gt;</c>
/// and calls <c>ZonePilot reload</c>. Other values in the file are kept.
/// </summary>
public sealed class ZonePilotViewModel : ObservableObject, IPage
{
    private static readonly TimeSpan CliTimeout = TimeSpan.FromSeconds(15);

    private readonly IConfigStore _config;
    private readonly IToolCli _cli;
    private readonly ILauncher _launcher;
    private string? _toolId;
    private string _status = string.Empty;
    private bool _busy;
    private bool _canSave;
    private string _defaultLayoutId = ZonePilotSettings.BuiltInLayouts[0].Id;
    private bool _dragEnabled = true;
    private string _dragModifier = "Shift";
    private bool _hotkeysEnabled = true;
    private string _monitorHint = string.Empty;

    public ZonePilotViewModel(IConfigStore config, IToolCli cli, ILauncher launcher)
    {
        _config = config;
        _cli = cli;
        _launcher = launcher;
        SaveCommand = new AsyncCommand(SaveAsync);
        RefreshCommand = new AsyncCommand(LoadAsync);
        OpenFileCommand = new AsyncCommand(async () =>
        {
            var r = await _launcher.OpenPathAsync(SettingsPath);
            if (!r.Success) Status = r.Error ?? "Could not open settings.json.";
        });
        EditorCommand = new AsyncCommand(async () =>
        {
            if (Tool is { } t) await _launcher.RunActionAsync(t, new ToolAction { Label = "Editor", Kind = ActionKind.Arguments, Arguments = "editor" });
        });
    }

    public ICommand SaveCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenFileCommand { get; }
    public ICommand EditorCommand { get; }

    public ObservableCollection<ZonePilotLayout> Layouts { get; } = [];
    /// <summary>The layouts plus "Default layout" (empty id), for the monitors.</summary>
    public ObservableCollection<ZonePilotLayout> MonitorChoices { get; } = [];
    public ObservableCollection<ZonePilotMonitorVM> Monitors { get; } = [];
    public ObservableCollection<ZonePilotHotkeyVM> Hotkeys { get; } = [];
    public static IReadOnlyList<string> DragModifiers => ZonePilotSettings.DragModifiers;

    public string SettingsPath => ZonePilotSettings.DefaultPath;

    /// <summary>The ZonePilot entry this page works for (the one that opened it, else the first one).</summary>
    public ToolEntry? Tool =>
        _config.Current.Tools.FirstOrDefault(t => t.Id == _toolId)
        ?? _config.Current.Tools.FirstOrDefault(t => string.Equals(t.Preset, ToolPresets.ZonePilot.Key, StringComparison.OrdinalIgnoreCase));

    /// <summary>ZonePilot.exe: the tool's path, else the default install location.</summary>
    public string ExePath
    {
        get
        {
            var path = Tool is { } t ? PathTokens.Expand(t.Path, t).Trim().Trim('"') : string.Empty;
            if (path.Length > 0 && File.Exists(path)) return path;
            return Environment.ExpandEnvironmentVariables(ToolPresets.ZonePilot.DefaultPath);
        }
    }

    public bool HasExe => File.Exists(ExePath);
    public string Subtitle => $"Settings of ZonePilot ({SettingsPath}). Saving writes the file and calls ZonePilot reload; other values in the file are kept.";

    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsBusy { get => _busy; private set => Set(ref _busy, value); }
    public bool CanSave { get => _canSave; private set => Set(ref _canSave, value); }
    public string MonitorHint { get => _monitorHint; private set => Set(ref _monitorHint, value); }

    public string DefaultLayoutId { get => _defaultLayoutId; set => Set(ref _defaultLayoutId, value ?? _defaultLayoutId); }
    public bool DragEnabled { get => _dragEnabled; set => Set(ref _dragEnabled, value); }
    public string DragModifier { get => _dragModifier; set => Set(ref _dragModifier, ZonePilotSettings.NormalizeModifier(value)); }
    public bool HotkeysEnabled { get => _hotkeysEnabled; set => Set(ref _hotkeysEnabled, value); }

    /// <summary>Called before the page is shown for a specific tool.</summary>
    public void UseTool(ToolEntry tool) => _toolId = tool.Id;

    public void OnShown() => _ = LoadAsync();

    private async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            OnPropertyChanged(nameof(ExePath));
            OnPropertyChanged(nameof(HasExe));
            ZonePilotSettings settings;
            try
            {
                settings = await Task.Run(() => ZonePilotSettings.Load(SettingsPath));
                CanSave = true;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                CanSave = false;
                Status = $"settings.json cannot be read ({ex.Message}). DevPilot leaves it alone; fix it with Open file, or let ZonePilot recreate it.";
                return;
            }

            ZonePilotLayoutList? reported = null;
            if (HasExe)
            {
                var r = await _cli.RunAsync(ExePath, ZonePilotSettings.LayoutsArguments, CliTimeout);
                reported = r.Ok ? ZonePilotSettings.ParseLayouts(r.Output) : null;
            }

            Fill(settings, reported);
            Status = File.Exists(SettingsPath) ? string.Empty : "settings.json does not exist yet: ZonePilot's defaults are shown; saving creates it.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Fill(ZonePilotSettings settings, ZonePilotLayoutList? reported)
    {
        Layouts.Clear();
        var layouts = reported is { Layouts.Count: > 0 } ? reported.Layouts : settings.Layouts();
        foreach (var l in layouts) Layouts.Add(l);
        MonitorChoices.Clear();
        MonitorChoices.Add(new ZonePilotLayout(string.Empty, "Default layout", true));
        foreach (var l in layouts) MonitorChoices.Add(l);

        DefaultLayoutId = settings.DefaultLayoutId;
        OnPropertyChanged(nameof(DefaultLayoutId)); // the combo lost its selection when its items were replaced
        DragEnabled = settings.DragEnabled;
        DragModifier = settings.DragModifier;
        HotkeysEnabled = settings.HotkeysEnabled;

        Hotkeys.Clear();
        foreach (var (key, label) in ZonePilotSettings.HotkeyFields)
            Hotkeys.Add(new ZonePilotHotkeyVM(key, label, settings.GetHotkey(key)));
        Hotkeys.Add(new ZonePilotHotkeyVM("snapToZoneModifiers", "Send the window to zone 1–9: modifiers + number", settings.SnapToZoneModifiers, modifiersOnly: true));

        Monitors.Clear();
        foreach (var m in reported?.Monitors ?? [])
            Monitors.Add(new ZonePilotMonitorVM(m, settings.MonitorLayoutFor(m.Key)));
        MonitorHint = Monitors.Count > 0
            ? "Applied with ZonePilot apply <layout> --monitor <n> (saved even when ZonePilot is not running)."
            : HasExe
                ? "ZonePilot did not list the monitors (ZonePilot layouts failed). Try Refresh."
                : $"ZonePilot.exe was not found ({ExePath}), so the monitors cannot be listed. Install it with scripts\\install.cmd in its repository.";
    }

    private async Task SaveAsync()
    {
        if (IsBusy || !CanSave) return;
        var invalid = Hotkeys.Where(h => !h.Validate()).ToList();
        if (invalid.Count > 0)
        {
            Status = $"Not saved: check {string.Join(", ", invalid.Select(h => h.Label.ToLowerInvariant()))}.";
            return;
        }

        IsBusy = true;
        var messages = new List<string>();
        try
        {
            ZonePilotMonitorPlan plan;
            try
            {
                // Read again: ZonePilot may have saved the file since the page was loaded.
                plan = await Task.Run(() =>
                {
                    var s = ZonePilotSettings.Load(SettingsPath);
                    s.DefaultLayoutId = DefaultLayoutId;
                    s.DragEnabled = DragEnabled;
                    s.DragModifier = DragModifier;
                    s.HotkeysEnabled = HotkeysEnabled;
                    foreach (var h in Hotkeys)
                    {
                        if (h.ModifiersOnly) s.SnapToZoneModifiers = h.Text;
                        else s.SetHotkey(h.Key, h.Text);
                    }
                    var p = s.PlanMonitorChanges(Monitors.Select(m => m.Monitor), Monitors.ToDictionary(m => m.Monitor.Index, m => m.LayoutId));
                    foreach (var key in p.ClearKeys) s.ClearMonitorLayout(key);
                    s.Save(SettingsPath);
                    return p;
                });
                messages.Add("settings.json saved.");
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                Status = $"Not saved: {ex.Message}";
                return;
            }

            if (!HasExe)
            {
                messages.Add($"ZonePilot.exe not found ({ExePath}): reload and per-monitor layouts skipped; ZonePilot reads the file at its next start.");
            }
            else
            {
                // Reload first, so the running app does not write its older copy over the file when it applies a layout.
                var reload = await _cli.RunAsync(ExePath, ZonePilotSettings.ReloadArguments, CliTimeout);
                messages.Add(reload.Ok ? "ZonePilot reloaded."
                    : reload.ExitCode == ZonePilotSettings.ExitNotRunning ? "ZonePilot is not running: it uses the new settings at its next start."
                    : $"Reload failed: {Describe(reload)}");

                foreach (var args in plan.ApplyArguments)
                {
                    var r = await _cli.RunAsync(ExePath, args, CliTimeout);
                    if (!r.Ok) messages.Add($"{args}: {Describe(r)}");
                }
                if (plan.ApplyArguments.Count > 0) messages.Add($"Per-monitor layouts applied ({plan.ApplyArguments.Count}).");
            }
        }
        finally
        {
            IsBusy = false;
        }
        await LoadAsync();
        Status = string.Join(" ", messages);
    }

    private static string Describe(ToolCliResult r) =>
        r.StartError ?? (r.TimedOut ? "timed out" : (r.Output.Length > 0 ? r.Output.Split('\n')[^1].Trim() : $"exit code {r.ExitCode}"));
}
