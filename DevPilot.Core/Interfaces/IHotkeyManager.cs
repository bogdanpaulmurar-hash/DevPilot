// ============================================================
// DevPilot — IT Automation Hub v1.0
// IHotkeyManager.cs — Contract pentru Global Hotkeys
// Phase: 6
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System;
using DevPilot.Models;

namespace DevPilot.Core.Interfaces;

/// <summary>
/// Contract pentru gestionarea hotkey-urilor globale Win32.
/// Implementarea primeste HWND din UI layer via Initialize().
/// </summary>
public interface IHotkeyManager : IDisposable
{
    /// <summary>Fired pe UI thread cand un hotkey inregistrat este apasat.</summary>
    event EventHandler<string>? HotkeyPressed;

    /// <summary>Initializeaza managerul cu HWND-ul ferestrei si un dispatcher UI.</summary>
    void Initialize(IntPtr hwnd, Action<Action> uiInvoke);

    /// <summary>Hook pentru WM_HOTKEY — adaugat via HwndSource.AddHook.</summary>
    IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled);

    /// <summary>Inregistreaza un hotkey.</summary>
    void Register(HotkeyConfig config);

    /// <summary>Dezinregistreaza toate hotkey-urile.</summary>
    void UnregisterAll();
}
