using DevPilot.Models;

namespace DevPilot.Core.Interfaces;

public interface IHotkeyManager : IDisposable
{
    event EventHandler<HotkeyConfig>? HotkeyPressed;
    void Initialize(IntPtr hwnd);
    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled);
    /// <summary>Registers all enabled hotkeys. Returns the ones that failed (already used by another app).</summary>
    IReadOnlyList<HotkeyConfig> RegisterAll(IEnumerable<HotkeyConfig> hotkeys);
    void UnregisterAll();
}
