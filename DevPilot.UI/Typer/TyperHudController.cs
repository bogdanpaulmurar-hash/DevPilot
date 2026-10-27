using DevPilot.Core.Interfaces;

namespace DevPilot.UI.Typer;

/// <summary>Owns the single HUD window: created on first use, hidden (not destroyed) when closed.</summary>
public sealed class TyperHudController
{
    private readonly IConfigStore _config;
    private readonly IKeyboardSender _sender;
    private readonly IForegroundProbe _probe;
    private TyperHudWindow? _window;

    public TyperHudController(IConfigStore config, IKeyboardSender sender, IForegroundProbe probe)
    {
        _config = config;
        _sender = sender;
        _probe = probe;
    }

    /// <summary>Shows the HUD with a fresh clipboard preview; a second call while it is idle hides it.</summary>
    public void Toggle()
    {
        _window ??= new TyperHudWindow(_config, _sender, _probe);
        if (_window.IsVisible && !_window.IsRunning) _window.Hide();
        else _window.Present();
    }
}
