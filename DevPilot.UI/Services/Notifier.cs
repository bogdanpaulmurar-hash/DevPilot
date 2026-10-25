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

    private static Action? _onClick;
    private static DateTime _onClickUntil;
    private static bool _clickHooked;

    /// <summary>Warning balloon; clicking it (within 60 s) runs <paramref name="onClick"/>. Balloons have no buttons.</summary>
    public static void Action(string title, string message, Action onClick)
    {
        Logger?.LogWarning("{Title}: {Message}", title, message);
        var app = System.Windows.Application.Current;
        if (app is null) return;
        app.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (Tray is null) return;
                if (!_clickHooked)
                {
                    _clickHooked = true;
                    Tray.TrayBalloonTipClicked += (_, _) =>
                    {
                        var run = DateTime.UtcNow <= _onClickUntil ? _onClick : null;
                        _onClick = null;
                        run?.Invoke();
                    };
                }
                _onClick = onClick;
                _onClickUntil = DateTime.UtcNow.AddSeconds(60);
                Tray.ShowBalloonTip(title, message, BalloonIcon.Warning);
            }
            catch (Exception) { /* balloon tips may be disabled by policy */ }
        });
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
