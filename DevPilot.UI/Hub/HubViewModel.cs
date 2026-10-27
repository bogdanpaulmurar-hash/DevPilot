using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DevPilot.Core.Config;
using DevPilot.Core.Folders;
using DevPilot.Core.Hub;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Launch;
using DevPilot.Core.Presets;
using DevPilot.Models;
using DevPilot.UI.Mvvm;
using DevPilot.UI.Services;

namespace DevPilot.UI.Hub;

/// <summary>State of the tray hub: groups, tools, spotlight, search, keep-awake toggle and appearance.</summary>
public sealed class HubViewModel : ObservableObject
{
    private static readonly string[] CaptureExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".mp4", ".webm"];

    private readonly IConfigStore _config;
    private readonly ILauncher _launcher;
    private readonly IProcessMonitor _monitor;
    private readonly IKeepAwakeService _keepAwake;
    private readonly IAppShell _shell;
    private string _search = string.Empty;
    private SpotlightVM? _spotlight;
    private bool _isPinned;
    private int _toolCount;

    public FlyoutBuilder Flyouts { get; }
    public ObservableCollection<HubGroupVM> Groups { get; } = [];

    public ICommand ToggleKeepAwakeCommand { get; }
    public ICommand OpenMainCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand AddToolCommand { get; }
    public ICommand OpenTyperCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand ClearSearchCommand { get; }

    public HubViewModel(IConfigStore config, ILauncher launcher, IProcessMonitor monitor, IKeepAwakeService keepAwake,
        IAppShell shell, FlyoutBuilder flyouts, DevPilot.UI.Typer.TyperHudController typer)
    {
        _config = config;
        _launcher = launcher;
        _monitor = monitor;
        _keepAwake = keepAwake;
        _shell = shell;
        Flyouts = flyouts;

        ToggleKeepAwakeCommand = new RelayCommand(() =>
        {
            _config.Current.KeepAwake.Enabled = !_config.Current.KeepAwake.Enabled;
            _config.Touch(ConfigArea.KeepAwake);
        });
        OpenMainCommand = new RelayCommand(() => { _shell.HideHub(); _shell.ShowMainWindow(); });
        OpenSettingsCommand = new RelayCommand(() => { _shell.HideHub(); _shell.ShowMainWindow("appearance"); });
        AddToolCommand = new RelayCommand(() => { _shell.HideHub(); _shell.ShowMainWindow("tools"); });
        OpenTyperCommand = new RelayCommand(() => { if (!IsPinned) _shell.HideHub(); typer.Toggle(); });
        ExitCommand = new RelayCommand(() => _shell.Exit());
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);

        _config.Changed += (_, area) =>
        {
            if (area is ConfigArea.All or ConfigArea.Tools or ConfigArea.Groups or ConfigArea.Hub or ConfigArea.Theme)
                Application.Current.Dispatcher.BeginInvoke(Rebuild);
            if (area is ConfigArea.All or ConfigArea.KeepAwake)
                Application.Current.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(KeepAwakeOn)));
        };
        _monitor.StatusChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(RefreshStatus);
        _keepAwake.StateChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            OnPropertyChanged(nameof(KeepAwakeOn));
            OnPropertyChanged(nameof(KeepAwakeStatus));
        });
        Rebuild();
    }

    // ── Appearance ───────────────────────────────────────────────────────
    public HubAppearance Look => _config.Current.Hub;
    public double PanelWidth => Look.Width;
    public double PanelMaxHeight => Look.MaxHeight;
    public CornerRadius PanelCorner => new(Look.CornerRadius);
    public double PanelOpacity => Look.Opacity;
    public double BaseFontSize => Look.FontSize;
    public Thickness RowPadding => Look.Density switch
    {
        HubDensity.Compact => new Thickness(10, 4, 10, 4),
        HubDensity.Spacious => new Thickness(12, 10, 12, 10),
        _ => new Thickness(10, 7, 10, 7),
    };
    public bool ShowSearch => Look.ShowSearch;

    // ── State ────────────────────────────────────────────────────────────
    public SpotlightVM? Spotlight { get => _spotlight; private set { Set(ref _spotlight, value); OnPropertyChanged(nameof(HasSpotlight)); } }
    public bool HasSpotlight => _spotlight is not null && string.IsNullOrWhiteSpace(_search);
    public bool KeepAwakeOn
    {
        get => _config.Current.KeepAwake.Enabled;
        set
        {
            if (_config.Current.KeepAwake.Enabled == value) return;
            _config.Current.KeepAwake.Enabled = value;
            _config.Touch(ConfigArea.KeepAwake);
        }
    }
    public string KeepAwakeStatus => _keepAwake.StatusText;
    public bool IsEmpty => _toolCount == 0;
    public bool NoResults => _toolCount > 0 && !string.IsNullOrWhiteSpace(_search) && Groups.All(g => !g.HasVisibleTools);
    public bool IsPinned { get => _isPinned; set => Set(ref _isPinned, value); }

    public string SearchText
    {
        get => _search;
        set
        {
            if (!Set(ref _search, value ?? string.Empty)) return;
            ApplyFilter();
            OnPropertyChanged(nameof(HasSpotlight));
        }
    }

    public void Rebuild()
    {
        var s = _config.Current;
        Groups.Clear();
        SpotlightVM? spotlight = null;
        int count = 0;
        var theme = RowTheme.Current();
        foreach (var g in s.Groups.Where(g => !g.Hidden).OrderBy(g => g.Order))
        {
            var gvm = new HubGroupVM(g, s.Hub, _config, theme);
            foreach (var t in Ordering.ToolsOf(s, g.Id).Where(t => !t.Hidden))
            {
                if (t.IsSeparator)
                {
                    gvm.Tools.Add(new HubToolVM(t, s.Hub, false, theme));
                    continue;
                }
                var tvm = new HubToolVM(t, s.Hub, Flyouts.HasFlyout(t), theme);
                if (t.Spotlight && spotlight is null && s.Hub.Spotlight.Enabled)
                {
                    spotlight = BuildSpotlight(tvm);
                    continue;
                }
                gvm.Tools.Add(tvm);
                count++;
            }
            if (gvm.Tools.Any(t => !t.IsSeparator)) Groups.Add(gvm);
        }
        _toolCount = count + (spotlight is null ? 0 : 1);
        Spotlight = spotlight;
        RefreshStatus();
        ApplyFilter();
        OnPropertyChanged(string.Empty);
    }

    private SpotlightVM BuildSpotlight(HubToolVM tool)
    {
        var opts = _config.Current.Hub.Spotlight;
        var folder = string.IsNullOrWhiteSpace(opts.CapturesFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ScreenPilot")
            : PathTokens.Expand(opts.CapturesFolder, tool.Model);
        var vm = new SpotlightVM(tool, opts)
        {
            CapturesFolder = folder,
            OpenCommand = new AsyncCommand(async () =>
            {
                if (await FlyoutBuilder.Run(_launcher.StartAsync(tool.Model), tool.Name)) CloseIfConfigured(true);
            }),
            FolderCommand = new AsyncCommand(async () =>
            {
                if (await FlyoutBuilder.Run(_launcher.OpenPathAsync(folder), "captures folder")) CloseIfConfigured(true);
            }),
        };
        foreach (var a in tool.Model.Actions.Where(a => a.Featured && a.Kind != ActionKind.Separator).Take(6))
        {
            var action = a;
            vm.Featured.Add(new SpotlightActionVM
            {
                Label = action.Label,
                Glyph = string.IsNullOrWhiteSpace(action.Glyph) ? Glyphs.ForAction(action.Kind) : action.Glyph,
                Command = new AsyncCommand(async () =>
                {
                    // Hide first so the capture does not include the hub.
                    _shell.HideHub();
                    await FlyoutBuilder.Run(_launcher.RunActionAsync(tool.Model, action), $"{tool.Name} — {action.Label}");
                }),
            });
        }
        return vm;
    }

    /// <summary>Loads the latest captures (thumbnails decoded small, off the UI thread).</summary>
    public async Task RefreshRecentAsync()
    {
        var sp = Spotlight;
        if (sp is null || !sp.Options.ShowRecentCaptures || sp.Options.RecentCount <= 0) return;
        var folder = sp.CapturesFolder;
        int count = sp.Options.RecentCount;
        var items = await Task.Run(() =>
        {
            var list = new List<(string Path, BitmapSource? Thumb, bool Video)>();
            foreach (var f in FolderBrowser.RecentFiles(folder, count, CaptureExtensions))
            {
                bool video = f.Name.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || f.Name.EndsWith(".webm", StringComparison.OrdinalIgnoreCase);
                list.Add((f.FullPath, video ? null : LoadThumb(f.FullPath), video));
            }
            return list;
        });
        if (!ReferenceEquals(sp, Spotlight)) return;
        sp.Recent.Clear();
        foreach (var (path, thumb, video) in items)
        {
            var p = path;
            sp.Recent.Add(new RecentCaptureVM
            {
                Path = p,
                Name = Path.GetFileName(p),
                Thumb = thumb,
                IsVideo = video,
                OpenCommand = new AsyncCommand(async () =>
                {
                    if (await FlyoutBuilder.Run(_launcher.OpenPathAsync(p), Path.GetFileName(p))) CloseIfConfigured(true);
                }),
            });
        }
        sp.NotifyRecent();
    }

    private static BitmapSource? LoadThumb(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.DecodePixelWidth = 180;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void RefreshStatus()
    {
        foreach (var t in AllTools().Where(t => !t.IsSeparator))
        {
            var st = _monitor.Get(t.Model.Id);
            t.State = st.State;
            t.StatusText = st.State switch
            {
                ToolRunState.Running => "Running" + (string.IsNullOrEmpty(st.Detail) ? string.Empty : $" · {st.Detail}"),
                ToolRunState.Starting => "Starting…",
                ToolRunState.Failed => "Failed: " + st.Detail,
                ToolRunState.Stopped => "Not running",
                _ => string.Empty,
            };
        }
    }

    private IEnumerable<HubToolVM> AllTools()
    {
        foreach (var g in Groups)
            foreach (var t in g.Tools) yield return t;
        if (Spotlight is not null) yield return Spotlight.Tool;
    }

    private void ApplyFilter()
    {
        foreach (var g in Groups)
        {
            // Separators only make sense in the full list, not between search results.
            foreach (var t in g.Tools)
                t.IsVisible = t.IsSeparator ? string.IsNullOrWhiteSpace(_search) : HubSearch.Matches(t.Model, g.Model.Title, _search);
            g.HasVisibleTools = g.Tools.Any(t => t.IsVisible && !t.IsSeparator);
            g.RefreshCount();
        }
        OnPropertyChanged(nameof(NoResults));
    }

    /// <summary>First visible tool (Enter in the search box starts it).</summary>
    public HubToolVM? FirstMatch => Groups.Where(g => g.HasVisibleTools).SelectMany(g => g.Tools).FirstOrDefault(t => t.IsVisible && !t.IsSeparator);

    public async Task ActivateAsync(HubToolVM tool)
    {
        if (tool.IsSeparator) return;
        var ok = await FlyoutBuilder.Run(_launcher.StartAsync(tool.Model), tool.Name);
        CloseIfConfigured(ok);
    }

    public void CloseIfConfigured(bool success)
    {
        if (success && Look.CloseAfterLaunch && !IsPinned) _shell.HideHub();
    }
}
