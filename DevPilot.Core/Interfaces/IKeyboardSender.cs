using DevPilot.Core.Typer;

namespace DevPilot.Core.Interfaces;

/// <summary>Sends one typing step to the window that currently has keyboard focus.</summary>
public interface IKeyboardSender
{
    /// <summary>Returns false when the operating system rejected the input.</summary>
    bool Send(TypingStep step);
}

/// <summary>Identifies the window that currently receives keyboard input (0 = unknown).</summary>
public interface IForegroundProbe
{
    long GetForegroundWindow();
}
