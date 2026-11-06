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

/// <summary>One tool of DevPilot that Update may close and start again for a repository.</summary>
public sealed class RepoToolChoiceVM : ObservableObject
{
    private readonly RepoRowVM _row;
    private readonly ToolEntry _tool;
    private readonly bool _auto;

    public RepoToolChoiceVM(RepoRowVM row, ToolEntry tool, bool auto)
    {
        _row = row;
        _tool = tool;
        _auto = auto;
    }

    public string Name => string.IsNullOrWhiteSpace(_tool.Name) ? _tool.Id : _tool.Name;
    public string Tip => _auto ? "Found by its path in this repository" : "Close and start again on Update";

    /// <summary>Ticked = Update closes it and starts it again. Tools found by path are always on.</summary>
    public bool IsLinked
    {
        get => _auto || (_row.ConfigOrNull?.ToolIds.Contains(_tool.Id, StringComparer.OrdinalIgnoreCase) ?? false);
        set
        {
            if (_auto || value == IsLinked) return;
            var ids = _row.Config.ToolIds;
            if (value) ids.Add(_tool.Id); else ids.RemoveAll(i => string.Equals(i, _tool.Id, StringComparison.OrdinalIgnoreCase));
            OnPropertyChanged();
            _row.Edited();
        }
    }

    public bool CanEdit => !_auto;
}

/// <summary>One clone under the root: tick it for "Pull selected", run its action, edit its commands.</summary>
public sealed class RepoRowVM : ObservableObject
{
    private RepoPullResult? _pull;
    private RepoCommandResult? _command;
    private bool _busy;
    private bool _expanded;
    private string? _runningName;

    public RepoRowVM(ReposViewModel owner, string path, string name, string branch, RepoVersionInfo version, RepoPullResult? lastPull, RepoCommandResult? lastCommand, RepoUpdatePlan? plan)
    {
        Owner = owner;
        UpdatePlan = plan;
        Path = path;
        Name = name;
        Branch = branch;
        _version = version;
        _pull = lastPull;
        _command = lastCommand;
        var cfg = RepoConfigs.Find(owner.Settings, path);
        if (cfg is not null)
            foreach (var c in cfg.Commands) Commands.Add(new RepoCommandVM(this, c));
        RunCommand = new RelayCommand(() => owner.RunActions(this));
        OpenCommand = new RelayCommand(() => ReposViewModel.OpenFolder(Path));
        AddCommandCommand = new RelayCommand(AddCommand);
        ReinstallCommand = new RelayCommand(() => owner.RunInBackground(this, RepoAction.Update(), RepoUpdateMode.ReinstallOnly));
    }

    internal ReposViewModel Owner { get; }
    public string Path { get; }
    public string Name { get; }
    public string Branch { get; }
    public bool HasBranch => Branch.Length > 0;

    private RepoVersionInfo _version;

    /// <summary>Version in the clone's files (or the short commit); read again after a pull, update or command.</summary>
    public string Version => _version.Display;
    public string VersionTip => _version.Tip;
    public bool HasVersion => Version.Length > 0;

    /// <summary>Reads the version from the files again; true when it changed.</summary>
    internal bool ReadVersion()
    {
        var v = RepoVersion.Read(Path);
        if (v == _version) return false;
        _version = v;
        OnPropertyChanged(nameof(Version));
        OnPropertyChanged(nameof(VersionTip));
        OnPropertyChanged(nameof(HasVersion));
        return true;
    }

    /// <summary>
    /// A command in its own window is not waited for, so the version is read again every few seconds until it
    /// changes (the window did a pull), for at most ten minutes.
    /// </summary>
    internal async Task WatchVersionAsync()
    {
        for (int i = 0; i < 150; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(4));
            if (ReadVersion()) return;
        }
    }
    public ObservableCollection<RepoCommandVM> Commands { get; } = [];

    /// <summary>Every tool Update could close for this repository, with the ones found by path ticked and locked.</summary>
    public IReadOnlyList<RepoToolChoiceVM> ToolChoices =>
        Owner.AllTools.Where(t => t.IsProcessLike)
            .Select(t => new RepoToolChoiceVM(this, t, RepoUpdatePlanner.IsAutoMatch(Path, t)))
            .OrderByDescending(c => !c.CanEdit).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The command the row ran last (null after a pull or update); the sign-in fix runs it again.</summary>
    internal RepoCommand? LastCommand { get; set; }

    /// <summary>The last action was skipped because tracked files have local changes.</summary>
    public bool LocalChangesProblem => CommandIsNewer
        ? _command is { Status: RepoCommandStatus.Skipped } && RepoPull.IsLocalChangesText(_command.OutputTail)
        : _pull is { Status: RepoPullStatus.LocalChanges };

    /// <summary>The last action was an Update (so it has to install after the fix pulled).</summary>
    internal bool LastWasUpdate => CommandIsNewer && _command!.Name == "Update";

    /// <summary>The last action failed because git needed a sign-in, or was skipped for local changes: offer the fix.</summary>
    public bool CanFix => !Busy && (CommandIsNewer
        ? _command is { Status: RepoCommandStatus.Failed } && RepoCommandRunner.NeedsSignIn(_command.OutputTail) || LocalChangesProblem
        : _pull is { Status: RepoPullStatus.AuthFailed } || LocalChangesProblem);

    public ICommand FixCommand => _fix ??= new RelayCommand(() => { if (LocalChangesProblem) Owner.FixLocalChanges(this); else Owner.FixSignIn(this); });
    private ICommand? _fix;

    public ICommand ShowOutputCommand => _showOutput ??= new RelayCommand(ShowOutput);
    private ICommand? _showOutput;

    /// <summary>The full text of the last action, as a console would have shown it.</summary>
    public string Transcript => CommandIsNewer ? _command!.Transcript : _pull?.Transcript ?? string.Empty;

    private void ShowOutput()
    {
        if (Transcript.Length > 0) Views.OutputWindow.ShowText($"{Name} — output", Transcript);
    }

    public ICommand RunCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand AddCommandCommand { get; }
    public ICommand ReinstallCommand { get; }

    /// <summary>What Update would do here (found when the folder was scanned, refreshed after an edit).</summary>
    public RepoUpdatePlan? UpdatePlan { get; private set; }
    public string UpdateSummary => UpdatePlan?.Summary ?? string.Empty;
    public bool CanReinstall => UpdatePlan is { Available: true };

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

    internal RepoConfig Config => RepoConfigs.GetOrCreate(Owner.Settings, Path);
    internal RepoConfig? ConfigOrNull => RepoConfigs.Find(Owner.Settings, Path);

    public PullButton PullChoice
    {
        get => ConfigOrNull?.Pull ?? PullButton.Auto;
        set { if (value == PullChoice) return; Config.Pull = value; OnPropertyChanged(); Edited(); }
    }

    public bool UpdateEnabled
    {
        get => ConfigOrNull?.UpdateEnabled ?? true;
        set { if (value == UpdateEnabled) return; Config.UpdateEnabled = value; OnPropertyChanged(); Edited(); }
    }

    public bool RestartTools
    {
        get => ConfigOrNull?.RestartTools ?? true;
        set { if (value == RestartTools) return; Config.RestartTools = value; OnPropertyChanged(); Edited(); }
    }

    /// <summary>Empty = found automatically.</summary>
    public string InstallCommand
    {
        get => ConfigOrNull?.InstallCommand ?? string.Empty;
        set
        {
            value = (value ?? string.Empty).Trim();
            if (value == InstallCommand) return;
            Config.InstallCommand = value;
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
            if (!value) { _runningName = null; _step = null; Manual = false; }
            RefreshStatus();
            OnPropertyChanged(nameof(CanRun));
        }
    }

    /// <summary>True while a single action started from this row runs; the global state handler leaves such a row alone.</summary>
    internal bool Manual { get; private set; }

    /// <summary>What an Update is doing now ("Closing X…"), shown instead of the generic running text.</summary>
    public void SetStep(string text)
    {
        _runningName = null;
        _step = text;
        RefreshStatus();
    }

    private string? _step;

    /// <summary>Marks the row busy with one action: a command (its name) or a single pull (null).</summary>
    public void SetRunning(string? name)
    {
        _runningName = name;
        Manual = true;
        Busy = true;
    }

    public RepoPullResult? PullResult { get => _pull; set { _pull = value; ReadVersion(); RefreshStatus(); } }
    public RepoCommandResult? CommandResult { get => _command; set { _command = value; ReadVersion(); RefreshStatus(); } }

    // ── What the row's button does ───────────────────────────────────────
    public IReadOnlyList<RepoAction> Actions => RepoConfigs.Actions(Owner.Settings, Path, UpdatePlan);

    public string ActionLabel
    {
        get
        {
            var a = Actions;
            return a.Count switch
            {
                0 => "No action",
                1 => a[0].IsPull ? "Pull" : a[0].Name,   // "Update" or the command's name
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
                1 => a[0].IsPull ? "git pull --ff-only" : a[0].IsUpdate ? "Update: " + UpdatePlan?.Summary : a[0].Command!.CommandLine,
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
        RefreshPlan();
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(ActionTip));
        OnPropertyChanged(nameof(CanRun));
    }

    internal void RefreshCanRun() => OnPropertyChanged(nameof(CanRun));

    /// <summary>Works out again what Update would do (after an edit or when the tools changed).</summary>
    internal void RefreshPlan()
    {
        UpdatePlan = RepoUpdatePlanner.Plan(Path, ConfigOrNull, Owner.AllTools);
        OnPropertyChanged(nameof(UpdatePlan));
        OnPropertyChanged(nameof(UpdateSummary));
        OnPropertyChanged(nameof(ToolChoices));
        OnPropertyChanged(nameof(CanReinstall));
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(ActionTip));
        OnPropertyChanged(nameof(CanRun));
    }

    // ── Status line ──────────────────────────────────────────────────────
    private bool CommandIsNewer => _command is not null && (_pull is null || _command.At >= _pull.At);

    public string StatusText
    {
        get
        {
            if (Busy) return _step is not null ? _step : _runningName is null ? "Pulling…" : $"Running {_runningName}…";
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
                return _command!.ShowAllLines ? RepoCommandRunner.LastLines(_command.OutputTail, 8)
                     : _command.IsOk ? RepoCommandRunner.LastLines(_command.OutputTail, 1) : RepoCommandRunner.LastLines(_command.OutputTail, 6);
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
                    RepoCommandStatus.Busy or RepoCommandStatus.Skipped => "skipped",
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
        OnPropertyChanged(nameof(Transcript));
        OnPropertyChanged(nameof(CanFix));
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

    public static IReadOnlyList<Choice<PullButton>> PullChoices { get; } =
    [
        new(PullButton.Auto, "Automatic: only when there is no Update"),
        new(PullButton.Always, "Always, next to Update"),
        new(PullButton.Never, "Never"),
    ];

    /// <summary>The tools DevPilot manages (an update closes and starts those that run from the repository).</summary>
    internal IReadOnlyList<ToolEntry> AllTools => _config.Current.Tools;

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

        var items = actions.Select(a => FlyoutItem.Action(a.Name, a.IsPull ? Glyphs.Sync : a.IsUpdate ? Glyphs.Download : Glyphs.Play,
            () => { RunInBackground(row, a); return Task.FromResult(true); },
            detail: a.IsUpdate ? "pull, install" : a.Command?.Mode == RepoCommandMode.Window ? "window" : string.Empty)).ToList();
        ToolMenuWindow.ShowAtCursor(items, row.Name, row.HasBranch ? row.Branch : null, _config.Current.Hub);
    }

    /// <summary>
    /// The last action stopped at a sign-in git cannot ask for in a hidden run. Offers to take the user name out of the
    /// remote address (git then uses the sign-in saved for the site), or to run the command once in a window.
    /// </summary>
    internal async void FixSignIn(RepoRowVM row)
    {
        try
        {
            var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            string? url = await _repos.GetRemoteUrlAsync(row.Path);
            string? plain = RepoRemote.StripUser(url);
            if (plain is not null)
            {
                var text = $"{row.Name}: the remote address names a user, so git asks for that account's password.\n\n" +
                           $"Now:  {url}\nNew:  {plain}\n\n" +
                           "Yes: use the new address (git then uses the sign-in saved for the site) and run the action again. " +
                           $"To undo: git remote set-url origin {url}\n" +
                           "No: sign in once in a window instead.\nCancel: do nothing.";
                var answer = owner is null ? MessageBox.Show(text, "Fix sign-in", MessageBoxButton.YesNoCancel, MessageBoxImage.Question)
                                           : MessageBox.Show(owner, text, "Fix sign-in", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Cancel) return;
                if (answer == MessageBoxResult.Yes)
                {
                    if (!await _repos.SetRemoteUrlAsync(row.Path, plain))
                    {
                        Notifier.Error("Fix sign-in", "git did not accept the new address, or something else is running on this repository.");
                        return;
                    }
                    var again = row.LastCommand is { } c ? new RepoAction(RepoConfigs.DisplayName(c), c) : RepoAction.Pull();
                    await RunActionAsync(row, again);
                    return;
                }
            }
            else
            {
                var text = $"{row.Name}: git needs a sign-in. DevPilot opens a window and runs the action once; sign in there and git remembers it.\n\nOpen the window?";
                var answer = owner is null ? MessageBox.Show(text, "Fix sign-in", MessageBoxButton.OKCancel, MessageBoxImage.Question)
                                           : MessageBox.Show(owner, text, "Fix sign-in", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (answer != MessageBoxResult.OK) return;
            }
            var line = row.LastCommand?.CommandLine ?? "git pull --ff-only";
            var name = row.LastCommand is { } last ? RepoConfigs.DisplayName(last) : "git pull";
            await RunActionAsync(row, new RepoAction(name, new RepoCommand { Name = name, CommandLine = line, Mode = RepoCommandMode.Window }));
        }
        catch (Exception ex) { Notifier.Error("Fix sign-in", ex.Message); }
    }

    /// <summary>
    /// The last action was skipped because tracked files have local changes. Lists them and asks what to do: set them
    /// aside, pull and put them back; set them aside and pull (they stay in the stash); or nothing. Never deletes anything.
    /// </summary>
    internal async void FixLocalChanges(RepoRowVM row)
    {
        try
        {
            var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            var files = RepoPull.ChangedFiles(await _repos.GetChangesAsync(row.Path), 8);
            if (files.Length == 0)
            {
                Notifier.Info("Fix…", $"{row.Name}: no changed files any more. Run the action again.");
                return;
            }
            var text = $"{row.Name}: these tracked files have changes that are not committed, so DevPilot does not pull:\n\n{files}\n\n" +
                       "Yes: set them aside, pull, then put them back (best for files the tool rewrites itself, like a config).\n" +
                       "No: set them aside and pull, but keep them aside (git stash pop brings them back).\n" +
                       "Cancel: do nothing.\n\n" +
                       "Nothing is deleted. If a file keeps changing, stop tracking it in that repository (.gitignore).";
            var answer = owner is null ? MessageBox.Show(text, "Fix local changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question)
                                       : MessageBox.Show(owner, text, "Fix local changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return;
            bool wasUpdate = row.LastWasUpdate;
            row.SetRunning("Fix…");
            var r = await _repos.StashPullAsync(row.Path, answer == MessageBoxResult.Yes);
            row.Busy = false;
            if (r is null) { Notifier.Info("Repositories", "Something is already running on this repository."); return; }
            row.PullResult = r;
            if (!r.IsOk) { Notifier.Error($"{row.Name}: {r.StatusText.ToLowerInvariant()}", r.Message); return; }
            Notifier.Info($"{row.Name}: {r.StatusText.ToLowerInvariant()}", r.Status == RepoPullStatus.Updated ? r.Message : r.VersionChange);
            // The pull is done; an Update that was skipped still has to install what came.
            if (wasUpdate && r.Status == RepoPullStatus.Updated) await RunActionAsync(row, RepoAction.Update(), RepoUpdateMode.ReinstallOnly);
        }
        catch (Exception ex) { Notifier.Error("Fix local changes", ex.Message); }
    }

    internal async void RunInBackground(RepoRowVM row, RepoAction action, RepoUpdateMode mode = RepoUpdateMode.Full)
    {
        try { await RunActionAsync(row, action, mode); }
        catch (Exception ex) { Notifier.Error("Repositories", ex.Message); }
    }

    internal async Task RunActionAsync(RepoRowVM row, RepoAction action, RepoUpdateMode mode = RepoUpdateMode.Full)
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
            if (r.IsOk) Notifier.Info($"{row.Name}: {r.StatusText.ToLowerInvariant()}", r.Status == RepoPullStatus.Updated ? r.Message : r.VersionChange);
            else Notifier.Error($"{row.Name}: {r.StatusText.ToLowerInvariant()}", r.Message);
            return;
        }

        if (action.IsUpdate)
        {
            await RunUpdateAsync(row, mode);
            return;
        }

        row.SetRunning(action.Name);
        row.LastCommand = action.Command;
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
                _ = row.WatchVersionAsync();
                break;
            case RepoCommandStatus.Busy:
                Notifier.Info("Repositories", result.OutputTail);
                break;
            default:
                Notifier.Error($"{action.Name} failed in {row.Name}", $"{result.StatusText}\n{RepoCommandRunner.LastLines(result.OutputTail, 1)}".TrimEnd());
                break;
        }
    }

    private async Task RunUpdateAsync(RepoRowVM row, RepoUpdateMode mode)
    {
        string name = mode == RepoUpdateMode.Full ? "Update" : "Reinstall";
        row.SetRunning(name);
        var dispatcher = Application.Current.Dispatcher;
        var result = await _repos.UpdateAsync(row.Path, mode, text => dispatcher.BeginInvoke(() => row.SetStep($"{name}: {text}")));
        row.Busy = false;
        row.CommandResult = result;
        row.RefreshPlan();
        var body = RepoCommandRunner.LastLines(result.OutputTail, 4);
        switch (result.Status)
        {
            case RepoCommandStatus.Succeeded: Notifier.Info($"{row.Name}: {name.ToLowerInvariant()} done", body); break;
            case RepoCommandStatus.Skipped or RepoCommandStatus.Busy: Notifier.Info($"{row.Name}: {name.ToLowerInvariant()} skipped", body); break;
            default: Notifier.Error($"{row.Name}: {name.ToLowerInvariant()} failed", body); break;
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
        List<(string Path, string Name, string Branch, RepoVersionInfo Version, RepoUpdatePlan Plan)> found;
        var tools = _config.Current.Tools.ToList();
        var configs = Settings.Items.ToList();
        try
        {
            found = await Task.Run(() => RepoScanner.Find(root)
                .Select(p => (p, RepoScanner.DisplayName(root, p), RepoScanner.ReadBranch(p), RepoVersion.Read(p),
                              RepoUpdatePlanner.Plan(p, configs.FirstOrDefault(c => RepoScanner.SamePath(c.Path, p)), tools))).ToList());
        }
        catch (Exception ex)
        {
            found = [];
            Notifier.Error("Repositories", ex.Message);
        }
        // Keep expanded rows open across a rescan.
        var open = Items.Where(i => i.IsExpanded).Select(i => i.Path).ToList();
        Items.Clear();
        foreach (var (path, name, branch, version, plan) in found)
        {
            var row = new RepoRowVM(this, path, name, branch, version, _repos.LastResult(path), _repos.LastCommandResult(path), plan)
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
