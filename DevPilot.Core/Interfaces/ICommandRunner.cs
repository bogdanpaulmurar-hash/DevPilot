using DevPilot.Models;

namespace DevPilot.Core.Interfaces;

/// <summary>Outcome of a repository command. <see cref="Started"/>: it runs in its own console window and is not waited for.</summary>
public sealed record CommandRun(int ExitCode, string Output, bool TimedOut = false, bool Started = false, string? StartError = null);

/// <summary>Runs a command line through cmd.exe in a folder.</summary>
public interface ICommandRunner
{
    Task<CommandRun> RunAsync(RepoCommand command, string workingDirectory, TimeSpan timeout, CancellationToken ct = default);
}
