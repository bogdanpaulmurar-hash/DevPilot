// ============================================================
// DevPilot — IT Automation Hub v1.0
// App.xaml.cs — DI Container + Application entry point
// Phase: 12 (+TrayService registration + Initialize call)
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DevPilot.Core.Interfaces;
using DevPilot.Infrastructure.Services;
using DevPilot.UI.Services;
using DevPilot.UI.ViewModels;
using DevPilot.UI.Views;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DevPilot.UI;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    // ── Single Instance ──────────────────────────────────────────────────
    private static Mutex? _mutex;
    internal const string MutexName = "DevPilot_SingleInstance_Z003FU5C";

    // ── Tray ─────────────────────────────────────────────────────────────
    private TaskbarIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        // ── Global exception handlers — must be first ─────────────────────
        DispatcherUnhandledException             += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException    += OnUnobservedTaskException;

        _mutex = new Mutex(true, MutexName, out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("DevPilot is already running.", AppConstants.AppName,
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        // Load persisted settings before showing UI
        Services.GetRequiredService<ISettingsManager>().Load();

        // ── System Tray ───────────────────────────────────────────────────
        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        _trayIcon.DataContext = Services.GetRequiredService<TrayViewModel>();

        // Build the initial tray menu from saved ShowInTray settings.
        Services.GetRequiredService<TrayService>().Initialize(_trayIcon);

        // ── Main Window ───────────────────────────────────────────────────
        var mainWindow = Services.GetRequiredService<MainWindow>();
        // Always show the window on startup — even if MinimizeToTray is true.
        // The user can minimise to tray afterwards; we never launch invisible.
        mainWindow.Show();
        mainWindow.WindowState = WindowState.Normal;
        mainWindow.Activate();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // ── Logging ───────────────────────────────────────────────────────
        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Debug));

        // ── Infrastructure / Services ─────────────────────────────────────
        services.AddSingleton<ISettingsManager, SettingsManager>();
        services.AddSingleton<IJigglerService,  JigglerService>();
        services.AddSingleton<HeartbeatService>();
        services.AddSingleton<IHeartbeatService>(sp => sp.GetRequiredService<HeartbeatService>());
        services.AddSingleton<IScreenshotService, ScreenshotService>();
        services.AddSingleton<IHotkeyManager,   HotkeyManager>();
        services.AddSingleton<ILauncherService,  LauncherService>();
        services.AddSingleton<IStartupService,   StartupService>();

        // ── Tray Service (Phase 12) ───────────────────────────────────────
        // Registered as both concrete TrayService (for Initialize in App.xaml.cs)
        // and ITrayService (for injection into SettingsViewModel).
        services.AddSingleton<TrayService>();
        services.AddSingleton<ITrayService>(sp => sp.GetRequiredService<TrayService>());

        // ── ViewModels ────────────────────────────────────────────────────
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<AppLauncherViewModel>();
        services.AddSingleton<ExternalToolsViewModel>();
        services.AddSingleton<JigglerViewModel>();
        services.AddSingleton<ScreenshotViewModel>();
        services.AddSingleton<HeartbeatViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<TrayViewModel>();

        // ── Views ─────────────────────────────────────────────────────────
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        // Release mutex before exit so a restarted instance can acquire it immediately
        try { _mutex?.ReleaseMutex(); } catch { /* already released */ }
        _mutex?.Dispose();
        base.OnExit(e);
    }

    // ════════════════════════════════════════════════════════════════════════
    // GLOBAL EXCEPTION HANDLERS
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Catches unhandled exceptions on the WPF Dispatcher (UI) thread.
    /// Logs, shows an error dialog, and marks handled to prevent crash.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        TryLog<App>(LogLevel.Critical, e.Exception, "Unhandled UI-thread exception.");

        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\n" +
            "DevPilot will attempt to continue. If instability persists, restart the application.",
            $"{AppConstants.AppName} — Unexpected Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true; // prevents crash
    }

    /// <summary>
    /// Catches unhandled exceptions on background / ThreadPool threads.
    /// Logs; cannot prevent termination when <see cref="UnhandledExceptionEventArgs.IsTerminating"/> is true.
    /// </summary>
    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        TryLog<App>(LogLevel.Critical, e.ExceptionObject as Exception,
            "Unhandled domain exception. IsTerminating={IsTerminating}", e.IsTerminating);
    }

    /// <summary>
    /// Catches unobserved Task exceptions (fire-and-forget tasks that threw).
    /// Marks observed to prevent process termination (fixes <c>TaskCanceledException</c> on shutdown).
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        TryLog<App>(LogLevel.Warning, e.Exception,
            "Unobserved task exception — marking observed to prevent crash.");
        e.SetObserved(); // prevents abnormal exit
    }

    // ── Helper: resolve logger safely (Services may not be initialised yet)

    private static void TryLog<T>(LogLevel level, Exception? ex, string message, params object[] args)
    {
        try
        {
            // Build combined args: message as first positional arg, then the rest
            var allArgs = new object[args.Length + 1];
            allArgs[0] = message;
            args.CopyTo(allArgs, 1);
            Services?.GetService<ILogger<T>>()?.Log(level, ex, "{Message}", allArgs);
        }
        catch
        {
            // Logging must never crash an exception handler
        }
    }
}
