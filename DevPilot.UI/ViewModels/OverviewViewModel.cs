using System.Windows;
using System.Windows.Input;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Launch;
using DevPilot.Models;
using DevPilot.UI.Hub;
using DevPilot.UI.Mvvm;
using DevPilot.UI.Services;

namespace DevPilot.UI.ViewModels;

/// <summary>Dashboard: the same groups as the hub, as cards with status and quick controls.</summary>
public sealed class OverviewViewModel : ObservableObject, IPage
{
    private readonly ILauncher _launcher;
    private readonly IProcessMonitor _monitor;
    private readonly IKeepAwakeService _keepAwake;
    private readonly IStartupManager _startup;
    private readonly IConfigStore _config;
    private readonly IAppShell _shell;
    private int _conflicts;

    public HubViewModel Hub { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand AddToolsCommand { get; }
    public ICommand OpenStartupCommand { get; }
    public ICommand OpenHubCommand { get; }

    public OverviewViewModel(HubViewModel hub, ILauncher launcher, IProcessMonitor monitor, IKeepAwakeService keepAwake,
        IStartupManager startup, IConfigStore config, IAppShell shell)
    {
        Hub = hub;
        _launcher = launcher;
        _monitor = monitor;
        _keepAwake = keepAwake;
        _startup = startup;
        _config = config;
        _shell = shell;
        StartCommand = new AsyncCommand(async p => { if (p is HubToolVM t) await FlyoutBuilder.Run(_launcher.StartAsync(t.Model), t.Name); });
        StopCommand = new AsyncCommand(async p => { if (p is HubToolVM t) await _launcher.StopAsync(t.Model); });
        RestartCommand = new AsyncCommand(async p => { if (p is HubToolVM t) await FlyoutBuilder.Run(_launcher.RestartAsync(t.Model), t.Name); });
        EditCommand = new RelayCommand(p => { if (p is HubToolVM t) _shell.EditTool(t.Model); });
        AddToolsCommand = new RelayCommand(() => _shell.ShowMainWindow("tools"));
        OpenStartupCommand = new RelayCommand(() => _shell.ShowMainWindow("startup"));
        OpenHubCommand = new RelayCommand(() => _shell.ShowHub(pinned: true));
        _monitor.StatusChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(RunningText)));
        _keepAwake.StateChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(KeepAwakeText)));
    }

    public string ToolCountText => $"{_config.Current.Tools.Count(t => !t.Hidden && !t.IsSeparator)} tools in {_config.Current.Groups.Count} groups";
    public string RunningText
    {
        get
        {
            var tracked = _config.Current.Tools.Where(ProcessMatcher.CanTrack).ToList();
            int running = tracked.Count(t => _monitor.Get(t.Id).State == ToolRunState.Running);
            return $"{running} of {tracked.Count} running";
        }
    }
    public string KeepAwakeText => _keepAwake.StatusText;
    public int Conflicts { get => _conflicts; private set { Set(ref _conflicts, value); OnPropertyChanged(nameof(ConflictText)); OnPropertyChanged(nameof(HasConflicts)); } }
    public bool HasConflicts => _conflicts > 0;
    public string ConflictText => _conflicts == 1
        ? "1 tool still starts with Windows on its own"
        : $"{_conflicts} tools still start with Windows on their own";

    public void OnShown()
    {
        OnPropertyChanged(nameof(ToolCountText));
        OnPropertyChanged(nameof(RunningText));
        OnPropertyChanged(nameof(KeepAwakeText));
        _ = Task.Run(() =>
        {
            try { return _startup.FindConflicts().Count(c => c.Entry.CanRemove); }
            catch (Exception) { return 0; }
        }).ContinueWith(t => Application.Current.Dispatcher.BeginInvoke(() => Conflicts = t.Result), TaskScheduler.Default);
    }
}
