using System.Runtime.InteropServices;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Typer;

namespace DevPilot.Infrastructure.Services;

/// <summary>
/// Types into the focused window with SendInput. Characters go as Unicode events (KEYEVENTF_UNICODE), so they do
/// not depend on the keyboard layout; Enter and Tab go as real virtual keys because many editors ignore U+000A / U+0009.
/// </summary>
public sealed class KeyboardSender : IKeyboardSender
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_TAB = 0x09;
    private const uint MAPVK_VK_TO_VSC = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    // MOUSEINPUT is the largest member; it keeps INPUT at the size SendInput expects on 32- and 64-bit.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion U; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    public bool Send(TypingStep step)
    {
        var inputs = new INPUT[2];
        switch (step.Kind)
        {
            case TypingStepKind.Enter:
                Fill(inputs, VK_RETURN, (ushort)MapVirtualKey(VK_RETURN, MAPVK_VK_TO_VSC), 0);
                break;
            case TypingStepKind.Tab:
                Fill(inputs, VK_TAB, (ushort)MapVirtualKey(VK_TAB, MAPVK_VK_TO_VSC), 0);
                break;
            default:
                Fill(inputs, 0, step.Char, KEYEVENTF_UNICODE);
                break;
        }
        return SendInput(2, inputs, InputSize) == 2;
    }

    private static void Fill(INPUT[] inputs, ushort vk, ushort scan, uint flags)
    {
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].U.ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags };
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].U.ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags | KEYEVENTF_KEYUP };
    }
}

/// <summary>Reports which top-level window currently has the keyboard focus.</summary>
public sealed class ForegroundProbe : IForegroundProbe
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public long Current() => GetForegroundWindow().ToInt64();

    long IForegroundProbe.GetForegroundWindow() => Current();
}
