namespace DevPilot.Core.Interfaces;

/// <summary>Outcome of a command shown in its own console window. <see cref="Error"/>: it could not be started.</summary>
public sealed record WindowRun(int ExitCode, bool TimedOut = false, string? Error = null);

/// <summary>
/// Runs one cmd.exe line in a visible console window and waits for it, with prompts allowed (unlike
/// <see cref="IGitRunner"/> and hidden commands), so git can ask for a sign-in there.
/// </summary>
public interface ISignInWindow
{
    Task<WindowRun> RunAsync(string workingDirectory, string commandLine, TimeSpan timeout, CancellationToken ct = default);
}
