using System.Runtime.InteropServices;
using DevPilot.Core.Interfaces;
using DevPilot.Models;
using Microsoft.Extensions.Logging;

namespace DevPilot.Infrastructure.Services;

/// <summary>Global hotkeys via RegisterHotKey. The UI passes its window handle and forwards WndProc messages.</summary>
public sealed class HotkeyManager : IHotkeyManager
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008, MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private static readonly Dictionary<string, uint> Keys = BuildKeys();

    private static Dictionary<string, uint> BuildKeys()
    {
        var d = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        for (char c = 'A'; c <= 'Z'; c++) d[c.ToString()] = c;
        for (char c = '0'; c <= '9'; c++) d[c.ToString()] = c;
        for (int i = 1; i <= 24; i++) d["F" + i] = (uint)(0x70 + i - 1);
        d["Space"] = 0x20; d["Enter"] = 0x0D; d["Escape"] = 0x1B; d["Tab"] = 0x09; d["Back"] = 0x08;
        d["Delete"] = 0x2E; d["Insert"] = 0x2D; d["Home"] = 0x24; d["End"] = 0x23; d["PageUp"] = 0x21; d["PageDown"] = 0x22;
        d["Left"] = 0x25; d["Up"] = 0x26; d["Right"] = 0x27; d["Down"] = 0x28; d["PrintScreen"] = 0x2C; d["Pause"] = 0x13;
        d["OemPlus"] = 0xBB; d["OemMinus"] = 0xBD; d["OemComma"] = 0xBC; d["OemPeriod"] = 0xBE;
        for (int i = 0; i <= 9; i++) d["NumPad" + i] = (uint)(0x60 + i);
        return d;
    }

    public static IReadOnlyCollection<string> KnownKeys => Keys.Keys;
    public static bool IsValidKey(string key) => !string.IsNullOrWhiteSpace(key) && Keys.ContainsKey(key.Trim());

    private readonly ILogger<HotkeyManager> _logger;
    private readonly Dictionary<int, HotkeyConfig> _registered = [];
    private IntPtr _hwnd;

    public event EventHandler<HotkeyConfig>? HotkeyPressed;

    public HotkeyManager(ILogger<HotkeyManager> logger) => _logger = logger;

    public void Initialize(IntPtr hwnd) => _hwnd = hwnd;

    public IReadOnlyList<HotkeyConfig> RegisterAll(IEnumerable<HotkeyConfig> hotkeys)
    {
        UnregisterAll();
        var failed = new List<HotkeyConfig>();
        if (_hwnd == IntPtr.Zero) return failed;
        foreach (var hk in hotkeys.Where(h => h.IsEnabled && !string.IsNullOrWhiteSpace(h.Action)))
        {
            if (!Keys.TryGetValue(hk.Key?.Trim() ?? string.Empty, out var vk)) { failed.Add(hk); continue; }
            uint mods = ParseModifiers(hk.Modifiers) | MOD_NOREPEAT;
            if (RegisterHotKey(_hwnd, hk.Id, mods, vk)) _registered[hk.Id] = hk;
            else
            {
                failed.Add(hk);
                _logger.LogWarning("Hotkey {Mods}+{Key} is already used by another application", hk.Modifiers, hk.Key);
            }
        }
        return failed;
    }

    public void UnregisterAll()
    {
        foreach (var id in _registered.Keys) UnregisterHotKey(_hwnd, id);
        _registered.Clear();
    }

    public IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _registered.TryGetValue(wParam.ToInt32(), out var hk))
        {
            handled = true;
            HotkeyPressed?.Invoke(this, hk);
        }
        return IntPtr.Zero;
    }

    public static uint ParseModifiers(string? modifiers)
    {
        uint result = 0;
        foreach (var part in (modifiers ?? string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result |= part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => MOD_CONTROL,
                "ALT" => MOD_ALT,
                "SHIFT" => MOD_SHIFT,
                "WIN" or "WINDOWS" => MOD_WIN,
                _ => 0u,
            };
        }
        return result;
    }

    public void Dispose() => UnregisterAll();
}
