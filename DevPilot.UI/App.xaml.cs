using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Launch;
using DevPilot.Infrastructure.Services;
using DevPilot.Models;
using DevPilot.UI.Hub;
using DevPilot.UI.Services;
using DevPilot.UI.ViewModels;
using DevPilot.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DevPilot.UI;

public partial class App : Application
{
    private const string MutexName = @"Local\DevPilot.SingleInstance.v2";
    private const string ActivateEventName = @"Local\DevPilot.Activate.v2";

    private static ServiceProvider? _services;
    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private HwndSource? _hotkeyWindow;
    private DispatcherTimer? _guardTimer;
    private ILogger<App>? _log;

    public static string LogFile { get; private set; } = string.Empty;
    public static bool StartedByWindows { get; private set; }

    public static T GetService<T>() where T : notnull => _services!.GetRequiredService<T>();

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            // Already running: ask the running instance to show itself.
            try { EventWaitHandle.OpenExisting(ActivateEventName).Set(); } catch (WaitHandleCannotBeOpenedException) { }
            Shutdown();
            return;
        }
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        new Thread(() =>
        {
            while (_activateEvent.WaitOne())
                Dispatcher.BeginInvoke(() => GetService<IAppShell>().ShowMainWindow());
        }) { IsBackground = true, Name = "DevPilot activation" }.Start();

        base.OnStartup(e);
        StartedByWindows = e.Args.Any(a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase));
        int smokeIdx = Array.FindIndex(e.Args, a => a.Equals("--smoke", StringComparison.OrdinalIgnoreCase));
        string? smokeDir = smokeIdx >= 0 && smokeIdx + 1 < e.Args.Length ? e.Args[smokeIdx + 1] : null;
        if (smokeDir is not null)
        {
            Environment.SetEnvironmentVariable("DEVPILOT_DATA_DIR", Path.Combine(smokeDir, "data"));
            SmokeRunner.Attach();
        }

        var fileLog = new FileLoggerProvider();
        LogFile = fileLog.FilePath;
        _services = BuildServices(fileLog);
        _log = GetService<ILogger<App>>();
        Notifier.Logger = _log;
        DispatcherUnhandledException += (_, ex) =>
        {
            _log.LogError(ex.Exception, "Unhandled UI exception");
            Notifier.Error("DevPilot", ex.Exception.Message);
            ex.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            _log.LogWarning(ex.Exception, "Unobserved task exception");
            ex.SetObserved();
        };

        var config = GetService<IConfigStore>();
        config.Load();
        LaunchPlanner.AppVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "2.0.0";
        _log.LogInformation("DevPilot {Version} started (autostart={Auto})", LaunchPlanner.AppVersion, StartedByWindows);

        GetService<ThemeService>().Apply(config.Current);

        var shell = GetService<IAppShell>();
        GetService<FlyoutBuilder>().EditTool = shell.EditTool;
        GetService<TrayManager>().Initialize(shell);

        var keepAwake = GetService<IKeepAwakeService>();
        keepAwake.Apply(config.Current.KeepAwake);

        var monitor = GetService<IProcessMonitor>();
        var launcher = GetService<ILauncher>();
        monitor.AutoRestartRequested += (_, tool) => Dispatcher.BeginInvoke(async () =>
        {
            var r = await launcher.StartAsync(tool, LaunchReason.Restart);
            if (!r.Success) Notifier.Error($"{tool.Name} stopped", "Auto-restart failed: " + r.Error);
            else Notifier.Info($"{tool.Name} restarted", "It had stopped unexpectedly.");
        });
        monitor.Start();

        SetupHotkeys();
        GetService<DevPilot.UI.Typer.ClipboardWatcher>().SetEnabled(config.Current.Typer.KeepClipboardHistory);
        GetService<DevPilot.Core.Typer.ClipboardImageStore>().Cleanup();
        SetupStartupGuard();
        config.Changed += OnConfigChanged;

        bool hidden = (StartedByWindows && config.Current.Startup.StartHidden) || config.Current.Startup.AlwaysStartHidden;
        if (!hidden) shell.ShowMainWindow(config.Current.Tools.Count == 0 ? "tools" : null);
        if (config.LoadWarning is not null)
            MessageBox.Show(config.LoadWarning, "DevPilot", MessageBoxButton.OK, MessageBoxImage.Warning);

        if (smokeDir is not null)
        {
            Dispatcher.BeginInvoke(async () => await SmokeRunner.RunAsync(smokeDir), DispatcherPriority.ApplicationIdle);
            return;
        }
        _ = StartAutoStartToolsAsync();
        _ = RunAutomaticBackupAsync();
    }

    // ── Automatic backup ─────────────────────────────────────────────────
    private async Task RunAutomaticBackupAsync()
    {
        try
        {
            // Let the tools start first; the backup itself runs in the background.
            await Task.Delay(TimeSpan.FromSeconds(10));
            var r = await GetService<BackupService>().RunAutomaticIfDueAsync();
            if (r is { Success: false }) Notifier.Error("Backup failed", r.Error ?? string.Empty);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Automatic backup failed");
        }
    }

    /// <summary>"Back up now" from the tray menu or a shortcut.</summary>
    public static async Task BackupNowAsync()
    {
        var r = await GetService<BackupService>().BackupNowAsync();
        if (r.Success) Notifier.Info("Backup made", Path.GetFileName(r.Path) ?? string.Empty);
        else Notifier.Error("Backup failed", r.Error ?? string.Empty);
    }

    private static ServiceProvider BuildServices(FileLoggerProvider fileLog)
    {
        var s = new ServiceCollection();
        s.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Information);
            b.AddProvider(fileLog);
        });
        var dataDir = Environment.GetEnvironmentVariable("DEVPILOT_DATA_DIR");
        s.AddSingleton(sp => string.IsNullOrWhiteSpace(dataDir)
            ? new ConfigStore(sp.GetRequiredService<ILogger<ConfigStore>>())
            : new ConfigStore(dataDir, sp.GetRequiredService<ILogger<ConfigStore>>()));
        s.AddSingleton<IConfigStore>(sp => sp.GetRequiredService<ConfigStore>());
        s.AddSingleton<IProcessMonitor, ProcessMonitor>();
        s.AddSingleton<IGuardGate, GuardGate>();
        s.AddSingleton<ILauncher, Launcher>();
        s.AddSingleton<IKeepAwakeService, KeepAwakeService>();
        s.AddSingleton<IStartupManager, StartupManager>();
        s.AddSingleton<BackupService>();
        s.AddSingleton<IHotkeyManager, HotkeyManager>();
        s.AddSingleton<IKeyboardSender, KeyboardSender>();
        s.AddSingleton<IForegroundProbe, ForegroundProbe>();
        s.AddSingleton<DevPilot.Core.Typer.ClipboardHistory>();
        s.AddSingleton<DevPilot.UI.Typer.ClipboardWatcher>();
        s.AddSingleton(_ => new DevPilot.Core.Typer.ClipboardImageStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevPilot", "clipboard-images")));
        s.AddSingleton<DevPilot.UI.Typer.TyperHudController>();
        s.AddSingleton<ThemeService>();
        s.AddSingleton<FlyoutBuilder>();
        s.AddSingleton<TrayManager>();
        s.AddSingleton<AppShell>();
        s.AddSingleton<IAppShell>(sp => sp.GetRequiredService<AppShell>());
        s.AddSingleton<HubViewModel>();
        s.AddSingleton<OverviewViewModel>();
        s.AddSingleton<ToolsViewModel>();
        s.AddSingleton<KeepAwakeViewModel>();
        s.AddSingleton<StartupViewModel>();
        s.AddSingleton<MonitorViewModel>();
        s.AddSingleton<AppearanceViewModel>();
        s.AddSingleton<SettingsViewModel>();
        s.AddSingleton<GuideViewModel>();
        s.AddSingleton<MainViewModel>();
        return s.BuildServiceProvider();
    }

    private void OnConfigChanged(object? sender, ConfigArea area)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var s = GetService<IConfigStore>().Current;
            if (area is ConfigArea.All or ConfigArea.Theme or ConfigArea.Hub)
                GetService<ThemeService>().Apply(s);
            if (area is ConfigArea.All or ConfigArea.KeepAwake)
                GetService<IKeepAwakeService>().Apply(s.KeepAwake);
            if (area is ConfigArea.All or ConfigArea.Hotkeys or ConfigArea.Tools)
                RegisterHotkeys();
            if (area is ConfigArea.All or ConfigArea.Startup)
                RestartGuardTimer();
        });
    }

    // ── Hotkeys ──────────────────────────────────────────────────────────
    private void SetupHotkeys()
    {
        var p = new HwndSourceParameters("DevPilotHotkeys") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) };
        _hotkeyWindow = new HwndSource(p);
        var hk = GetService<IHotkeyManager>();
        hk.Initialize(_hotkeyWindow.Handle);
        _hotkeyWindow.AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) => hk.WndProc(hwnd, msg, w, l, ref handled));
        hk.HotkeyPressed += (_, config) => Dispatcher.BeginInvoke(() => RunHotkey(config));
        RegisterHotkeys();
    }

    private void RegisterHotkeys()
    {
        var failed = GetService<IHotkeyManager>().RegisterAll(GetService<IConfigStore>().Current.Hotkeys);
        GetService<SettingsViewModel>().MarkFailed(failed);
    }

    private async void RunHotkey(HotkeyConfig hk)
    {
        var shell = GetService<IAppShell>();
        var config = GetService<IConfigStore>();
        var launcher = GetService<ILauncher>();
        var parts = hk.Action.Split(':');
        switch (parts[0])
        {
            case "hub": shell.ToggleHub(); break;
            case "main": shell.ShowMainWindow(); break;
            case "keepawake":
                config.Current.KeepAwake.Enabled = !config.Current.KeepAwake.Enabled;
                config.Touch(ConfigArea.KeepAwake);
                Notifier.Info("Keep-awake", config.Current.KeepAwake.Enabled ? "On" : "Off");
                break;
            case "typer": GetService<DevPilot.UI.Typer.TyperHudController>().Toggle(); break;
            case "backup": await BackupNowAsync(); break;
            case "tool" when parts.Length >= 2:
                if (config.Current.Tools.FirstOrDefault(t => t.Id == parts[1]) is { } tool)
                    await FlyoutBuilder.Run(launcher.StartAsync(tool), tool.Name);
                break;
            case "action" when parts.Length >= 3:
                if (config.Current.Tools.FirstOrDefault(t => t.Id == parts[1]) is { } owner
                    && owner.Actions.FirstOrDefault(a => a.Id == parts[2]) is { } action)
                    await FlyoutBuilder.Run(launcher.RunActionAsync(owner, action), $"{owner.Name} — {action.Label}");
                break;
        }
    }

    // ── Conflict guard ───────────────────────────────────────────────────
    private void SetupStartupGuard()
    {
        var config = GetService<IConfigStore>();
        var startup = GetService<IStartupManager>();
        try
        {
            // Registered from outside (install script): adopt it. Enabled but missing (moved exe): register again.
            bool registered = startup.IsDevPilotRegistered;
            if (registered && !config.Current.Startup.StartWithWindows)
            {
                config.Current.Startup.StartWithWindows = true;
                config.Touch(ConfigArea.Startup);
            }
            else if (!registered && config.Current.Startup.StartWithWindows)
                startup.SetDevPilotStartup(true);
            startup.EnforceTakeovers();
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Startup guard failed");
        }

        // Tools such as ScreenPilot rewrite their autostart entry when they start.
        GetService<ILauncher>().ToolStarted += (_, tool) =>
        {
            if (!config.Current.Startup.TakenOver.Any(t => t.ToolId == tool.Id)) return;
            _ = Task.Delay(TimeSpan.FromSeconds(20)).ContinueWith(_ => Enforce(), TaskScheduler.Default);
            _ = Task.Delay(TimeSpan.FromSeconds(90)).ContinueWith(_ => Enforce(), TaskScheduler.Default);
        };
        RestartGuardTimer();
    }

    private void Enforce()
    {
        try
        {
            int n = GetService<IStartupManager>().EnforceTakeovers();
            if (n > 0) _log?.LogInformation("Conflict guard removed {Count} re-created autostart entr(ies)", n);
        }
        catch (Exception ex) { _log?.LogWarning(ex, "Conflict guard failed"); }
    }

    private void RestartGuardTimer()
    {
        var minutes = GetService<IConfigStore>().Current.Startup.RecheckMinutes;
        _guardTimer?.Stop();
        if (minutes <= 0) return;
        _guardTimer ??= new DispatcherTimer();
        _guardTimer.Interval = TimeSpan.FromMinutes(minutes);
        _guardTimer.Tick -= GuardTick;
        _guardTimer.Tick += GuardTick;
        _guardTimer.Start();
    }

    private void GuardTick(object? sender, EventArgs e) => Task.Run(Enforce);

    // ── Autostart of tools ───────────────────────────────────────────────
    private async Task StartAutoStartToolsAsync()
    {
        var config = GetService<IConfigStore>();
        var launcher = GetService<ILauncher>();
        var monitor = GetService<IProcessMonitor>();
        var tools = config.Current.Tools.Where(t => t.AutoStart.Enabled).OrderBy(t => t.AutoStart.Order).ToList();
        if (tools.Count == 0) return;
        var started = Stopwatch.StartNew();
        foreach (var tool in tools)
        {
            var wait = TimeSpan.FromSeconds(Math.Max(0, tool.AutoStart.DelaySeconds)) - started.Elapsed;
            if (wait > TimeSpan.Zero) await Task.Delay(wait);
            if (await Task.Run(() => monitor.FindProcesses(tool)).ConfigureAwait(true) is { Count: > 0 })
            {
                _log?.LogInformation("Autostart: {Tool} is already running", tool.Name);
                continue;
            }
            var r = await launcher.StartAsync(tool, LaunchReason.AutoStart);
            if (!r.Success) Notifier.Error($"Could not start {tool.Name}", r.Error ?? string.Empty);
        }
    }

    // ── Exit ─────────────────────────────────────────────────────────────
    private bool _cleaned;

    public void Cleanup()
    {
        if (_cleaned) return;
        _cleaned = true;
        try
        {
            _guardTimer?.Stop();
            _services?.GetService<IHotkeyManager>()?.Dispose();
            _hotkeyWindow?.Dispose();
            _services?.GetService<DevPilot.UI.Typer.ClipboardWatcher>()?.Dispose();
            _services?.GetService<TrayManager>()?.Dispose();
            _services?.GetService<IKeepAwakeService>()?.Dispose();
            _services?.GetService<IProcessMonitor>()?.Dispose();
            _services?.GetService<ConfigStore>()?.Dispose();
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Cleanup failed");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
