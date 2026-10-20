namespace DevPilot.Core.Launch;

public enum LaunchMode
{
    /// <summary>CreateProcess with our environment block (managed tools).</summary>
    Process,
    /// <summary>ShellExecute (folders, URLs, documents, run-as-admin).</summary>
    ShellOpen,
}

/// <summary>Everything needed to start something — computed without side effects so it can be tested.</summary>
public sealed class LaunchPlan
{
    public LaunchMode Mode { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string Arguments { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public bool RunAsAdmin { get; init; }
    public bool HideWindow { get; init; }
    /// <summary>Console programs that should get their own visible console window.</summary>
    public bool NewConsole { get; init; }
    /// <summary>True when this starts the tool's long-running main process (tracked by the monitor).</summary>
    public bool IsMainProcess { get; init; }
    /// <summary>
    /// Wait a moment after starting and report a failure when the process ends at once with an error
    /// (its stderr is captured). Used for windowless Python, which otherwise fails silently.
    /// </summary>
    public bool WatchStartup { get; init; }

    public string CommandLine => string.IsNullOrEmpty(Arguments)
        ? CommandLineText.Quote(FileName)
        : CommandLineText.Quote(FileName) + " " + Arguments;

    public override string ToString() => $"{Mode}: {CommandLine}";
}
