using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Logging;

namespace DevPilot.UI.Services;

/// <summary>Balloon notifications through the main tray icon (errors from launches, conflict guard info).</summary>
public static class Notifier
{
    public static TaskbarIcon? Tray { get; set; }
    public static ILogger? Logger { get; set; }

    public static void Info(string title, string message)
    {
        Logger?.LogInformation("{Title}: {Message}", title, message);
        Show(title, message, BalloonIcon.Info);
    }

    public static void Error(string title, string message)
    {
        Logger?.LogWarning("{Title}: {Message}", title, message);
        Show(title, message, BalloonIcon.Error);
    }

    private static void Show(string title, string message, BalloonIcon icon)
    {
        var app = System.Windows.Application.Current;
        if (app is null) return;
        app.Dispatcher.BeginInvoke(() =>
        {
            try { Tray?.ShowBalloonTip(title, message, icon); }
            catch (Exception) { /* balloon tips may be disabled by policy */ }
        });
    }
}
