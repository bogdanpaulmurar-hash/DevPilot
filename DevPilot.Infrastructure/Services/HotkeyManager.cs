// ============================================================
// DevPilot — IT Automation Hub v1.0
// HotkeyManager.cs — Global Hotkeys via Win32 RegisterHotKey
// Phase: 6
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DevPilot.Core.Interfaces;
using DevPilot.Models;
using Microsoft.Extensions.Logging;

namespace DevPilot.Infrastructure.Services;

/// <summary>
/// Gestioneaza hotkey-urile globale ale aplicatiei via RegisterHotKey Win32.
/// Fara dependente WPF — primeste HWND ca IntPtr din UI layer.
/// </summary>
public sealed class HotkeyManager : IHotkeyManager
{
    // ── Win32 ─────────────────────────────────────────────────────────────
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ── Modifier flags ────────────────────────────────────────────────────
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    // ── VK lookup table (fara KeyInterop / WPF) ───────────────────────────
    private static readonly Dictionary<string, uint> VkMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Letters A-Z → VK = ASCII uppercase
        ["A"] = 0x41,
        ["B"] = 0x42,
        ["C"] = 0x43,
        ["D"] = 0x44,
        ["E"] = 0x45,
        ["F"] = 0x46,
        ["G"] = 0x47,
        ["H"] = 0x48,
        ["I"] = 0x49,
        ["J"] = 0x4A,
        ["K"] = 0x4B,
        ["L"] = 0x4C,
        ["M"] = 0x4D,
        ["N"] = 0x4E,
        ["O"] = 0x4F,
        ["P"] = 0x50,
        ["Q"] = 0x51,
        ["R"] = 0x52,
        ["S"] = 0x53,
        ["T"] = 0x54,
        ["U"] = 0x55,
        ["V"] = 0x56,
        ["W"] = 0x57,
        ["X"] = 0x58,
        ["Y"] = 0x59,
        ["Z"] = 0x5A,
        // Digits 0-9
        ["0"] = 0x30,
        ["1"] = 0x31,
        ["2"] = 0x32,
        ["3"] = 0x33,
        ["4"] = 0x34,
        ["5"] = 0x35,
        ["6"] = 0x36,
        ["7"] = 0x37,
        ["8"] = 0x38,
        ["9"] = 0x39,
        // Function keys
        ["F1"] = 0x70,
        ["F2"] = 0x71,
        ["F3"] = 0x72,
        ["F4"] = 0x73,
        ["F5"] = 0x74,
        ["F6"] = 0x75,
        ["F7"] = 0x76,
        ["F8"] = 0x77,
        ["F9"] = 0x78,
        ["F10"] = 0x79,
        ["F11"] = 0x7A,
        ["F12"] = 0x7B,
        // Special
        ["Space"] = 0x20,
        ["Enter"] = 0x0D,
        ["Escape"] = 0x1B,
        ["Tab"] = 0x09,
        ["Back"] = 0x08,
        ["Delete"] = 0x2E,
        ["Insert"] = 0x2D,
        ["Home"] = 0x24,
        ["End"] = 0x23,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["Left"] = 0x25,
        ["Up"] = 0x26,
        ["Right"] = 0x27,
        ["Down"] = 0x28,
        ["PrintScreen"] = 0x2C,
        ["Pause"] = 0x13,
        ["NumLock"] = 0x90,
    };

    // ── State ─────────────────────────────────────────────────────────────
    private readonly ILogger<HotkeyManager> _logger;
    private readonly Dictionary<int, HotkeyConfig> _registered = [];
    private IntPtr _hwnd;
    private Action<Action>? _uiInvoke;
    private bool _initialized;
    private bool _disposed;

    // ── Events ────────────────────────────────────────────────────────────
    public event EventHandler<string>? HotkeyPressed;

    // ── Constructor ───────────────────────────────────────────────────────
    public HotkeyManager(ILogger<HotkeyManager> logger)
    {
        _logger = logger;
    }

    // ── API ───────────────────────────────────────────────────────────────

    public void Initialize(IntPtr hwnd, Action<Action> uiInvoke)
    {
        _hwnd = hwnd;
        _uiInvoke = uiInvoke;
        _initialized = true;
        _logger.LogInformation("HotkeyManager initialized with HWND={Hwnd}", hwnd);
    }

    public void Register(HotkeyConfig config)
    {
        if (!_initialized)
        {
            _logger.LogWarning("HotkeyManager.Register called before Initialize — skipped.");
            return;
        }
        if (!config.IsEnabled) return;

        uint mods = ParseModifiers(config.Modifiers) | MOD_NOREPEAT;
        uint vk = ParseKey(config.Key);

        if (vk == 0)
        {
            _logger.LogWarning("Invalid key '{Key}' for hotkey '{Action}' — skipped.", config.Key, config.Action);
            return;
        }

        if (_registered.ContainsKey(config.Id))
            UnregisterHotKey(_hwnd, config.Id);

        bool ok = RegisterHotKey(_hwnd, config.Id, mods, vk);
        if (ok)
        {
            _registered[config.Id] = config;
            _logger.LogInformation("Hotkey registered: Id={Id} [{Mods}+{Key}] → {Action}",
                config.Id, config.Modifiers, config.Key, config.Action);
        }
        else
        {
            int err = Marshal.GetLastWin32Error();
            _logger.LogWarning("RegisterHotKey FAILED: Id={Id} [{Mods}+{Key}] Win32Error={Err}",
                config.Id, config.Modifiers, config.Key, err);
        }
    }

    public void UnregisterAll()
    {
        foreach (var id in _registered.Keys)
            UnregisterHotKey(_hwnd, id);
        _registered.Clear();
        _logger.LogInformation("All hotkeys unregistered.");
    }

    public IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_initialized) return IntPtr.Zero;

        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_registered.TryGetValue(id, out var config))
            {
                _logger.LogDebug("WM_HOTKEY: Id={Id} Action={Action}", id, config.Action);
                _uiInvoke?.Invoke(() => HotkeyPressed?.Invoke(this, config.Action));
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static uint ParseModifiers(string modifiers)
    {
        uint result = 0;
        foreach (var part in modifiers.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            result |= part.Trim().ToUpperInvariant() switch
            {
                "CTRL" => MOD_CONTROL,
                "CONTROL" => MOD_CONTROL,
                "ALT" => MOD_ALT,
                "SHIFT" => MOD_SHIFT,
                "WIN" => MOD_WIN,
                _ => 0
            };
        }
        return result;
    }

    private static uint ParseKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return 0;
        return VkMap.TryGetValue(key.Trim(), out var vk) ? vk : 0;
    }

    // ── IDisposable ───────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_initialized) UnregisterAll();
    }
}
