using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Repos;
using DevPilot.Infrastructure.Services;
using DevPilot.Models;
using DevPilot.UI.Mvvm;
using DevPilot.UI.Services;
using Microsoft.Win32;

namespace DevPilot.UI.ViewModels;

/// <summary>One clone under the root: tick to include it in "Pull all", or pull it alone.</summary>
public sealed class RepoRowVM : ObservableObject
{
    private readonly ReposViewModel _owner;
    private RepoPullResult? _result;
    private bool _busy;

    public RepoRowVM(ReposViewModel owner, string path, string name, string branch, RepoPullResult? last)
    {
        _owner = owner;
        Path = path;
        Name = name;
        Branch = branch;
        _result = last;
        PullCommand = new AsyncCommand(() => _owner.PullOneAsync(this));
        OpenCommand = new RelayCommand(() => ReposViewModel.OpenFolder(Path));
    }

    public string Path { get; }
    public string Name { get; }
    public string Branch { get; }
    public bool HasBranch => Branch.Length > 0;
    public ICommand PullCommand { get; }
    public ICommand OpenCommand { get; }

    public bool IsSelected
    {
        get => !_owner.IsExcluded(Path);
        set
        {
            if (value == IsSelected) return;
            _owner.SetExcluded(Path, !value);
            OnPropertyChanged();
        }
    }

    public bool Busy { get => _busy; set { if (Set(ref _busy, value)) OnPropertyChanged(nameof(StatusText)); } }

    public RepoPullResult? Result
    {
        get => _result;
        set
        {
            _result = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(Message));
            OnPropertyChanged(nameof(HasMessage));
            OnPropertyChanged(nameof(Tone));
        }
    }

    public string StatusText => Busy ? "Pulling…" : _result is null ? string.Empty : $"{_result.StatusText} · {_result.At:t}";
    public string Message => _result is { IsOk: false } or { Status: RepoPullStatus.Updated } ? _result.Message : string.Empty;
    public bool HasMessage => Message.Length > 0;

    /// <summary>"ok", "updated", "skipped", "failed" or "none": only drives the small status mark.</summary>
    public string Tone => _result switch
    {
        null => "none",
        { Status: RepoPullStatus.Updated } => "updated",
        { IsOk: true } => "ok",
        { IsSkipped: true } => "skipped",
        _ => "failed",
    };
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

        _repos.StateChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(StatusLine));
            foreach (var row in Items) row.Busy = false;
            if (_repos.IsRunning)
                foreach (var row in Items.Where(r => r.IsSelected)) row.Busy = true;
        });
        _repos.RepoFinished += (_, r) => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (Items.FirstOrDefault(i => RepoScanner.SamePath(i.Path, r.Path)) is { } row)
            {
                row.Busy = false;
                row.Result = r;
            }
        });
        _config.Changed += (_, a) => { if (a == ConfigArea.All) Application.Current?.Dispatcher.BeginInvoke(OnShown); };
    }

    public RepoSettings Settings => _config.Current.Repos;
    public bool IsRunning => _repos.IsRunning;
    public bool IsIdle => !_repos.IsRunning;
    public bool Scanning { get => _scanning; private set { if (Set(ref _scanning, value)) OnPropertyChanged(nameof(StatusLine)); } }
    public bool HasRoot => !string.IsNullOrWhiteSpace(Settings.Root);
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

    internal async Task PullOneAsync(RepoRowVM row)
    {
        if (_repos.IsRunning) { Notifier.Info("Pull repositories", "A pull is already running."); return; }
        row.Busy = true;
        var r = await _repos.PullOneAsync(row.Path);
        row.Busy = false;
        if (r is not null) row.Result = r;
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
        Items.Clear();
        foreach (var (path, name, branch) in found)
        {
            var row = new RepoRowVM(this, path, name, branch, _repos.LastResult(path));
            if (_repos.IsRunning && row.IsSelected) row.Busy = true;
            Items.Add(row);
        }
        Scanning = false;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(StatusLine));
    }
}
