using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Presets;
using DevPilot.Core.Repos;
using DevPilot.Infrastructure.Services;
using DevPilot.Models;
using DevPilot.UI.Hub;
using DevPilot.UI.Mvvm;
using DevPilot.UI.Services;
using Microsoft.Win32;

namespace DevPilot.UI.ViewModels;

/// <summary>One command of a repository as edited on the Repositories page.</summary>
public sealed class RepoCommandVM : ObservableObject
{
    private readonly RepoRowVM _row;

    public RepoCommandVM(RepoRowVM row, RepoCommand model)
    {
        _row = row;
        Model = model;
        RunCommand = new RelayCommand(() => _row.Owner.RunInBackground(_row, new RepoAction(RepoConfigs.DisplayName(Model), Model)));
        RemoveCommand = new RelayCommand(() => _row.Remove(this));
    }

    public RepoCommand Model { get; }
    public ICommand RunCommand { get; }
    public ICommand RemoveCommand { get; }

    public string Name
    {
        get => Model.Name;
        set { if (Model.Name == value) return; Model.Name = value ?? string.Empty; OnPropertyChanged(); _row.Edited(); }
    }

    public string CommandLine
    {
        get => Model.CommandLine;
        set { if (Model.CommandLine == value) return; Model.CommandLine = value ?? string.Empty; OnPropertyChanged(); _row.Edited(); }
    }

    /// <summary>True = opens a console window that stays open; false = hidden, result shown in DevPilot.</summary>
    public bool InWindow
    {
        get => Model.Mode == RepoCommandMode.Window;
        set
        {
            var mode = value ? RepoCommandMode.Window : RepoCommandMode.Hidden;
            if (Model.Mode == mode) return;
            Model.Mode = mode;
            OnPropertyChanged();
            _row.Edited();
        }
    }
}

/// <summary>One clone under the root: tick it for "Pull selected", run its action, edit its commands.</summary>
public sealed class RepoRowVM : ObservableObject
{
    private RepoPullResult? _pull;
    private RepoCommandResult? _command;
    private bool _busy;
    private bool _expanded;
    private string? _runningName;

    public RepoRowVM(ReposViewModel owner, string path, string name, string branch, RepoPullResult? lastPull, RepoCommandResult? lastCommand)
    {
        Owner = owner;
        Path = path;
        Name = name;
        Branch = branch;
        _pull = lastPull;
        _command = lastCommand;
        var cfg = RepoConfigs.Find(owner.Settings, path);
        if (cfg is not null)
            foreach (var c in cfg.Commands) Commands.Add(new RepoCommandVM(this, c));
        RunCommand = new RelayCommand(() => owner.RunActions(this));
        OpenCommand = new RelayCommand(() => ReposViewModel.OpenFolder(Path));
        AddCommandCommand = new RelayCommand(AddCommand);
    }

    internal ReposViewModel Owner { get; }
    public string Path { get; }
    public string Name { get; }
    public string Branch { get; }
    public bool HasBranch => Branch.Length > 0;
    public ObservableCollection<RepoCommandVM> Commands { get; } = [];
    public ICommand RunCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand AddCommandCommand { get; }

    public bool IsSelected
    {
        get => !Owner.IsExcluded(Path);
        set
        {
            if (value == IsSelected) return;
            Owner.SetExcluded(Path, !value);
            OnPropertyChanged();
        }
    }

    public bool IsExpanded { get => _expanded; set => Set(ref _expanded, value); }

    public bool IncludePull
    {
        get => RepoConfigs.Find(Owner.Settings, Path)?.IncludePull ?? true;
        set
        {
            if (value == IncludePull) return;
            RepoConfigs.GetOrCreate(Owner.Settings, Path).IncludePull = value;
            OnPropertyChanged();
            Edited();
        }
    }

    public bool Busy
    {
        get => _busy;
        set
        {
            if (!Set(ref _busy, value)) return;
            if (!value) { _runningName = null; Manual = false; }
            RefreshStatus();
            OnPropertyChanged(nameof(CanRun));
        }
    }

    /// <summary>True while a single action started from this row runs; the global state handler leaves such a row alone.</summary>
    internal bool Manual { get; private set; }

    /// <summary>Marks the row busy with one action: a command (its name) or a single pull (null).</summary>
    public void SetRunning(string? name)
    {
        _runningName = name;
        Manual = true;
        Busy = true;
    }

    public RepoPullResult? PullResult { get => _pull; set { _pull = value; RefreshStatus(); } }
    public RepoCommandResult? CommandResult { get => _command; set { _command = value; RefreshStatus(); } }

    // ── What the row's button does ───────────────────────────────────────
    public IReadOnlyList<RepoAction> Actions => RepoConfigs.Actions(Owner.Settings, Path);

    public string ActionLabel
    {
        get
        {
            var a = Actions;
            return a.Count switch
            {
                0 => "No action",
                1 => a[0].IsPull ? "Pull" : a[0].Name,
                _ => "Run…",
            };
        }
    }

    public string ActionTip
    {
        get
        {
            var a = Actions;
            return a.Count switch
            {
                0 => "Turn on git pull or add a command in DevPilot > Repositories.",
                1 => a[0].IsPull ? "git pull --ff-only" : a[0].Command!.CommandLine,
                _ => "Choose what to run: " + string.Join(", ", a.Select(x => x.Name)),
            };
        }
    }

    public bool CanRun => !Busy && Owner.IsIdle && Actions.Count > 0;

    public void Remove(RepoCommandVM vm)
    {
        RepoConfigs.Find(Owner.Settings, Path)?.Commands.Remove(vm.Model);
        Commands.Remove(vm);
        Edited();
    }

    private void AddCommand()
    {
        var cfg = RepoConfigs.GetOrCreate(Owner.Settings, Path);
        var cmd = new RepoCommand();
        cfg.Commands.Add(cmd);
        Commands.Add(new RepoCommandVM(this, cmd));
        IsExpanded = true;
        Edited();
    }

    /// <summary>A setting of this repository changed: save and refresh what the button says.</summary>
    internal void Edited()
    {
        Owner.TouchRepos();
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(ActionTip));
        OnPropertyChanged(nameof(CanRun));
    }

    internal void RefreshCanRun() => OnPropertyChanged(nameof(CanRun));

    // ── Status line ──────────────────────────────────────────────────────
    private bool CommandIsNewer => _command is not null && (_pull is null || _command.At >= _pull.At);

    public string StatusText
    {
        get
        {
            if (Busy) return _runningName is null ? "Pulling…" : $"Running {_runningName}…";
            if (CommandIsNewer) return $"{_command!.Name}: {_command.StatusText} · {_command.At:t}";
            return _pull is null ? string.Empty : $"{_pull.StatusText} · {_pull.At:t}";
        }
    }

    public string Message
    {
        get
        {
            if (Busy) return string.Empty;
            if (CommandIsNewer)
                return _command!.IsOk ? RepoCommandRunner.LastLines(_command.OutputTail, 1) : RepoCommandRunner.LastLines(_command.OutputTail, 6);
            return _pull is { IsOk: false } or { Status: RepoPullStatus.Updated } ? _pull.Message : string.Empty;
        }
    }

    public bool HasMessage => Message.Length > 0;

    /// <summary>"ok", "updated", "skipped", "failed" or "none": only drives the small status mark.</summary>
    public string Tone
    {
        get
        {
            if (Busy) return "none";
            if (CommandIsNewer)
                return _command!.Status switch
                {
                    RepoCommandStatus.Succeeded or RepoCommandStatus.Started => "ok",
                    RepoCommandStatus.Busy => "skipped",
                    _ => "failed",
                };
            return _pull switch
            {
                null => "none",
                { Status: RepoPullStatus.Updated } => "updated",
                { IsOk: true } => "ok",
                { IsSkipped: true } => "skipped",
                _ => "failed",
            };
        }
    }

    private void RefreshStatus()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(Tone));
    }
}

public sealed class ReposViewModel : ObservableObject, IPage
{
    private readonly IConfigStore _config;
    private readonly RepoSyncService _repos;
    private readonly IAppShell _shell;
    private bool _scanning;

    public ObservableCollection<RepoRowVM> Items { get; } = [];
    public ICommand BrowseCommand { get; }
    public ICommand OpenRootCommand { get; }
    public ICommand RescanCommand { get; }
    public ICommand PullAllCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand SelectNoneCommand { get; }
    public ICommand OpenPageCommand { get; }

    public static IReadOnlyList<Choice<RepoAutoPull>> AutoModes { get; } =
    [
        new(RepoAutoPull.Off, "Off: only when I press Pull"),
        new(RepoAutoPull.AtStartup, "Once, when DevPilot starts"),
        new(RepoAutoPull.Interval, "When DevPilot starts, then every N minutes"),
    ];

    public ReposViewModel(IConfigStore config, RepoSyncService repos, IAppShell shell)
    {
        _config = config;
        _repos = repos;
        _shell = shell;
        BrowseCommand = new RelayCommand(Browse);
        OpenRootCommand = new RelayCommand(() => OpenFolder(Settings.Root), () => Directory.Exists(Settings.Root));
        RescanCommand = new RelayCommand(OnShown);
        PullAllCommand = new AsyncCommand(() => RepoActions.PullNowAsync(_repos, _shell));
        SelectAllCommand = new RelayCommand(() => SetAll(true));
        SelectNoneCommand = new RelayCommand(() => SetAll(false));
        OpenPageCommand = new RelayCommand(() => _shell.ShowMainWindow("repos"));

        _repos.StateChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(StatusLine));
            foreach (var row in Items)
            {
                // A pull of all marks the ticked rows busy; a single action marks its own row (RunActionAsync).
                if (row.Manual) { row.RefreshCanRun(); continue; }
                bool pulling = _repos.IsRunning && row.IsSelected;
                if (pulling) row.Busy = true;
                else if (!_repos.IsBusy(row.Path)) row.Busy = false;
                row.RefreshCanRun();
            }
        });
        _repos.RepoFinished += (_, r) => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (Items.FirstOrDefault(i => RepoScanner.SamePath(i.Path, r.Path)) is { } row)
            {
                row.Busy = false;
                row.PullResult = r;
            }
        });
        _config.Changed += (_, a) => { if (a == ConfigArea.All) Application.Current?.Dispatcher.BeginInvoke(OnShown); };
    }

    public RepoSettings Settings => _config.Current.Repos;
    public bool IsRunning => _repos.IsRunning;
    public bool IsIdle => !_repos.IsRunning;
    public bool Scanning { get => _scanning; private set { if (Set(ref _scanning, value)) OnPropertyChanged(nameof(StatusLine)); } }
    public bool HasRoot => !string.IsNullOrWhiteSpace(Settings.Root);
    public bool NoRoot => !HasRoot;
    public bool IsEmpty => HasRoot && !Scanning && Items.Count == 0;
    public bool ShowInterval => Settings.Auto == RepoAutoPull.Interval;

    public string RootText
    {
        get => Settings.Root;
        set
        {
            var v = (value ?? string.Empty).Trim().Trim('"');
            if (v == Settings.Root) return;
            Settings.Root = v;
            _config.Touch(ConfigArea.Repos);
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasRoot));
            OnPropertyChanged(nameof(NoRoot));
            OnShown();
        }
    }

    public RepoAutoPull Auto
    {
        get => Settings.Auto;
        set
        {
            if (Settings.Auto == value) return;
            Settings.Auto = value;
            _config.Touch(ConfigArea.Repos);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowInterval));
        }
    }

    public string StatusLine
    {
        get
        {
            if (IsRunning) return "Pulling…";
            if (Scanning) return "Looking for repositories…";
            if (!HasRoot) return "Choose the folder that holds your clones.";
            int selected = Items.Count(i => i.IsSelected);
            var last = Settings.LastRun is { } t ? $" · last pull {t:g}" : string.Empty;
            return $"{Items.Count} repositories found, {selected} selected{last}";
        }
    }

    internal void TouchRepos() => _config.Touch(ConfigArea.Repos);

    internal bool IsExcluded(string path) => Settings.Excluded.Any(e => RepoScanner.SamePath(e, path));

    internal void SetExcluded(string path, bool excluded)
    {
        Settings.Excluded.RemoveAll(e => RepoScanner.SamePath(e, path));
        if (excluded) Settings.Excluded.Add(RepoScanner.Normalize(path));
        _config.Touch(ConfigArea.Repos);
        OnPropertyChanged(nameof(StatusLine));
    }

    private void SetAll(bool on)
    {
        foreach (var row in Items) row.IsSelected = on;
    }

    // ── Running actions ──────────────────────────────────────────────────

    /// <summary>The row's button: runs the only action, or shows a menu of buttons when there are several.</summary>
    internal void RunActions(RepoRowVM row)
    {
        var actions = row.Actions;
        if (actions.Count == 0)
        {
            Notifier.Info("Repositories", "This repository has no action. Add a command in DevPilot > Repositories.");
            return;
        }
        if (actions.Count == 1) { RunInBackground(row, actions[0]); return; }

        var items = actions.Select(a => FlyoutItem.Action(a.Name, a.IsPull ? Glyphs.Sync : Glyphs.Play,
            () => { RunInBackground(row, a); return Task.FromResult(true); },
            detail: a.Command?.Mode == RepoCommandMode.Window ? "window" : string.Empty)).ToList();
        ToolMenuWindow.ShowAtCursor(items, row.Name, row.HasBranch ? row.Branch : null, _config.Current.Hub);
    }

    internal async void RunInBackground(RepoRowVM row, RepoAction action)
    {
        try { await RunActionAsync(row, action); }
        catch (Exception ex) { Notifier.Error("Repositories", ex.Message); }
    }

    internal async Task RunActionAsync(RepoRowVM row, RepoAction action)
    {
        if (_repos.IsRunning || row.Busy)
        {
            Notifier.Info("Repositories", "Something is already running on this repository.");
            return;
        }
        if (action.IsPull)
        {
            row.SetRunning(null);
            var r = await _repos.PullOneAsync(row.Path);
            row.Busy = false;
            if (r is null) return;
            row.PullResult = r;
            if (r.IsOk) Notifier.Info($"{row.Name}: {r.StatusText.ToLowerInvariant()}", r.Status == RepoPullStatus.Updated ? r.Message : string.Empty);
            else Notifier.Error($"{row.Name}: {r.StatusText.ToLowerInvariant()}", r.Message);
            return;
        }

        row.SetRunning(action.Name);
        var result = await _repos.RunCommandAsync(row.Path, action.Command!);
        row.Busy = false;
        row.CommandResult = result;
        switch (result.Status)
        {
            case RepoCommandStatus.Succeeded:
                Notifier.Info($"{action.Name}: done", $"{row.Name}\n{RepoCommandRunner.LastLines(result.OutputTail, 1)}".TrimEnd());
                break;
            case RepoCommandStatus.Started:
                Notifier.Info($"{action.Name} opened", $"{row.Name}: running in its own window.");
                break;
            case RepoCommandStatus.Busy:
                Notifier.Info("Repositories", result.OutputTail);
                break;
            default:
                Notifier.Error($"{action.Name} failed in {row.Name}", $"{result.StatusText}\n{RepoCommandRunner.LastLines(result.OutputTail, 1)}".TrimEnd());
                break;
        }
    }

    private void Browse()
    {
        var dlg = new OpenFolderDialog { Title = "Folder that holds your git clones" };
        if (Directory.Exists(Settings.Root)) dlg.InitialDirectory = Settings.Root;
        if (dlg.ShowDialog() != true) return;
        RootText = dlg.FolderName;
    }

    internal static void OpenFolder(string path)
    {
        try
        {
            if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Notifier.Error("Open folder", ex.Message); }
    }

    public async void OnShown()
    {
        OnPropertyChanged(nameof(RootText));
        OnPropertyChanged(nameof(HasRoot));
        OnPropertyChanged(nameof(NoRoot));
        OnPropertyChanged(nameof(Auto));
        OnPropertyChanged(nameof(ShowInterval));
        if (!HasRoot)
        {
            Items.Clear();
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(StatusLine));
            return;
        }
        if (Scanning) return;
        Scanning = true;
        var root = Settings.Root;
        List<(string Path, string Name, string Branch)> found;
        try
        {
            found = await Task.Run(() => RepoScanner.Find(root)
                .Select(p => (p, RepoScanner.DisplayName(root, p), RepoScanner.ReadBranch(p))).ToList());
        }
        catch (Exception ex)
        {
            found = [];
            Notifier.Error("Repositories", ex.Message);
        }
        // Keep expanded rows open across a rescan.
        var open = Items.Where(i => i.IsExpanded).Select(i => i.Path).ToList();
        Items.Clear();
        foreach (var (path, name, branch) in found)
        {
            var row = new RepoRowVM(this, path, name, branch, _repos.LastResult(path), _repos.LastCommandResult(path))
            {
                IsExpanded = open.Any(o => RepoScanner.SamePath(o, path)),
            };
            if (_repos.IsBusy(path) || (_repos.IsRunning && row.IsSelected)) row.Busy = true;
            Items.Add(row);
        }
        Scanning = false;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(StatusLine));
    }
}
