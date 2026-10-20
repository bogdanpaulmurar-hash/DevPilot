using DevPilot.Core.Presets;
using DevPilot.Models;

namespace DevPilot.Core.Launch;

public enum LaunchReason { Manual, AutoStart, Restart, Action }

/// <summary>Turns a tool (and optionally one of its actions) into a <see cref="LaunchPlan"/>.</summary>
public static class LaunchPlanner
{
    public const string DefaultPython = "pythonw.exe";
    public const string EnvManaged = "DEVPILOT_MANAGED";
    public const string EnvSuppress = "DEVPILOT_SUPPRESS";
    public const string EnvToolId = "DEVPILOT_TOOL_ID";
    public const string EnvVersion = "DEVPILOT_VERSION";

    public static string AppVersion { get; set; } = "2.1.0";

    /// <summary>File check used to find a preset's interpreter (replaceable in tests).</summary>
    public static Func<string, bool> FileExists { get; set; } = File.Exists;

    /// <summary>
    /// Python interpreter for a tool: the configured one, else the first existing interpreter of its preset
    /// (e.g. ScreenPilot's private venv), else pythonw.exe / python.exe from PATH.
    /// A tool installed in its own venv fails with "No module named …" under the PATH Python.
    /// </summary>
    public static string ResolveInterpreter(ToolEntry tool)
    {
        if (!string.IsNullOrWhiteSpace(tool.Interpreter))
            return PathTokens.Expand(tool.Interpreter, tool).Trim().Trim('"');
        string wanted = tool.HideWindow ? DefaultPython : "python.exe";
        var preset = ToolPresets.Find(tool.Preset) ?? ToolPresets.Recognise(tool);
        foreach (var candidate in preset?.Interpreters ?? Array.Empty<string>())
        {
            var path = PathTokens.Expand(candidate, tool).Trim().Trim('"');
            var dir = Path.GetDirectoryName(path);
            var exe = string.IsNullOrEmpty(dir) ? wanted : Path.Combine(dir, wanted);
            if (FileExists(exe)) return exe;
        }
        return wanted;
    }

    /// <summary>Plan for starting the tool itself.</summary>
    public static LaunchPlan ForTool(ToolEntry tool, LaunchReason reason = LaunchReason.Manual)
    {
        string args = PathTokens.Expand(tool.Arguments, tool);
        if (reason == LaunchReason.AutoStart && !string.IsNullOrWhiteSpace(tool.AutoStart.Arguments))
            args = PathTokens.Expand(tool.AutoStart.Arguments, tool);
        if (tool.Managed.Enabled && tool.Tray != TrayMode.Native && !string.IsNullOrWhiteSpace(tool.Managed.NoTrayArguments))
            args = CommandLineText.Join(args, PathTokens.Expand(tool.Managed.NoTrayArguments, tool));
        return Build(tool, args, isMain: true);
    }

    /// <summary>Plan for one of the tool's actions.</summary>
    public static LaunchPlan ForAction(ToolEntry tool, ToolAction action)
    {
        switch (action.Kind)
        {
            case ActionKind.Url:
            case ActionKind.Folder:
            case ActionKind.File:
            {
                var target = PathTokens.Expand(action.Value, tool);
                if (action.Kind == ActionKind.Folder)
                    return new LaunchPlan { Mode = LaunchMode.ShellOpen, FileName = "explorer.exe", Arguments = CommandLineText.Quote(target) };
                return new LaunchPlan
                {
                    Mode = LaunchMode.ShellOpen,
                    FileName = target,
                    Arguments = PathTokens.Expand(action.Arguments, tool),
                    WorkingDirectory = PathTokens.ToolDirectory(tool),
                };
            }
            case ActionKind.Command:
            {
                var program = PathTokens.Expand(action.Value, tool).Trim().Trim('"');
                return new LaunchPlan
                {
                    Mode = LaunchMode.Process,
                    FileName = program,
                    Arguments = PathTokens.Expand(action.Arguments, tool),
                    WorkingDirectory = PathTokens.ToolDirectory(tool),
                    Environment = ManagedEnvironment(tool),
                    HideWindow = tool.HideWindow,
                    NewConsole = !tool.HideWindow,
                };
            }
            case ActionKind.Separator:
                throw new InvalidOperationException("A separator cannot be launched.");
            default:
                return Build(tool, PathTokens.Expand(action.Arguments, tool), isMain: false);
        }
    }

    /// <summary>Plan that opens the location of the tool in Explorer (selecting the file when possible).</summary>
    public static LaunchPlan ForLocation(ToolEntry tool)
    {
        var path = PathTokens.Expand(tool.Path, tool).Trim('"');
        if (tool.IsFolderLike || string.IsNullOrWhiteSpace(path))
            return new LaunchPlan { Mode = LaunchMode.ShellOpen, FileName = "explorer.exe", Arguments = CommandLineText.Quote(tool.IsFolderLike ? path : PathTokens.ToolDirectory(tool)) };
        return new LaunchPlan { Mode = LaunchMode.ShellOpen, FileName = "explorer.exe", Arguments = "/select," + CommandLineText.Quote(path) };
    }

    private static LaunchPlan Build(ToolEntry tool, string args, bool isMain)
    {
        var path = PathTokens.Expand(tool.Path, tool).Trim().Trim('"');
        var workDir = PathTokens.ToolDirectory(tool);
        var env = ManagedEnvironment(tool);
        bool admin = tool.RunAsAdmin;
        LaunchMode processMode = admin ? LaunchMode.ShellOpen : LaunchMode.Process;

        switch (tool.Kind)
        {
            case ToolKind.Executable:
                return new LaunchPlan
                {
                    Mode = processMode, FileName = path, Arguments = args, WorkingDirectory = workDir,
                    Environment = env, RunAsAdmin = admin, HideWindow = tool.HideWindow, IsMainProcess = isMain,
                };

            case ToolKind.Python:
            {
                var interpreter = ResolveInterpreter(tool);
                string pyArgs = !string.IsNullOrWhiteSpace(tool.PythonModule)
                    ? CommandLineText.Join("-m", tool.PythonModule.Trim(), args)
                    : CommandLineText.Join(CommandLineText.Quote(path), args);
                return new LaunchPlan
                {
                    Mode = processMode, FileName = interpreter, Arguments = pyArgs, WorkingDirectory = workDir,
                    Environment = env, RunAsAdmin = admin, HideWindow = tool.HideWindow,
                    NewConsole = !tool.HideWindow, IsMainProcess = isMain,
                    // pythonw shows nothing when the script fails at start: watch the first seconds.
                    WatchStartup = tool.HideWindow && !admin,
                };
            }

            case ToolKind.PowerShell:
                return new LaunchPlan
                {
                    Mode = processMode, FileName = "powershell.exe",
                    Arguments = CommandLineText.Join("-NoProfile -ExecutionPolicy Bypass",
                        tool.HideWindow ? "-WindowStyle Hidden" : null, "-File", CommandLineText.Quote(path), args),
                    WorkingDirectory = workDir, Environment = env, RunAsAdmin = admin,
                    HideWindow = tool.HideWindow, NewConsole = !tool.HideWindow, IsMainProcess = isMain,
                };

            case ToolKind.Batch:
                return new LaunchPlan
                {
                    Mode = processMode, FileName = "cmd.exe",
                    Arguments = "/c \"" + CommandLineText.Join(CommandLineText.Quote(path), args) + "\"",
                    WorkingDirectory = workDir, Environment = env, RunAsAdmin = admin,
                    HideWindow = tool.HideWindow, NewConsole = !tool.HideWindow, IsMainProcess = isMain,
                };

            case ToolKind.VBScript:
                return new LaunchPlan
                {
                    Mode = processMode, FileName = "wscript.exe",
                    Arguments = CommandLineText.Join(CommandLineText.Quote(path), args),
                    WorkingDirectory = workDir, Environment = env, RunAsAdmin = admin, IsMainProcess = isMain,
                };

            case ToolKind.ShellCommand:
                return new LaunchPlan
                {
                    Mode = processMode, FileName = "cmd.exe",
                    Arguments = (tool.HideWindow ? "/c " : "/k ") + CommandLineText.Join(path, args),
                    WorkingDirectory = workDir, Environment = env, RunAsAdmin = admin,
                    HideWindow = tool.HideWindow, NewConsole = !tool.HideWindow, IsMainProcess = isMain,
                };

            case ToolKind.Ssh:
                return new LaunchPlan
                {
                    Mode = processMode, FileName = "ssh.exe", Arguments = CommandLineText.Join(path, args),
                    Environment = env, NewConsole = true, IsMainProcess = isMain,
                };

            case ToolKind.Wsl:
                return new LaunchPlan
                {
                    Mode = processMode, FileName = "wsl.exe", Arguments = CommandLineText.Join(path, args),
                    Environment = env, NewConsole = true, IsMainProcess = isMain,
                };

            case ToolKind.Folder:
            case ToolKind.NetworkShare:
                return new LaunchPlan { Mode = LaunchMode.ShellOpen, FileName = "explorer.exe", Arguments = CommandLineText.Quote(path) };

            case ToolKind.Url:
                return new LaunchPlan { Mode = LaunchMode.ShellOpen, FileName = path };

            case ToolKind.Document:
                return new LaunchPlan { Mode = LaunchMode.ShellOpen, FileName = path, WorkingDirectory = workDir };

            default:
                throw new NotSupportedException($"Kind '{tool.Kind}' is not supported.");
        }
    }

    /// <summary>The variables that tell a tool DevPilot owns tray / keep-awake / autostart.</summary>
    public static IReadOnlyDictionary<string, string> ManagedEnvironment(ToolEntry tool)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (tool.Managed.Enabled && tool.IsProcessLike)
        {
            env[EnvManaged] = "1";
            env[EnvSuppress] = SuppressList(tool);
            env[EnvToolId] = tool.Id;
            env[EnvVersion] = AppVersion;
        }
        foreach (var line in (tool.Managed.ExtraEnvironment ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            var key = line[..idx].Trim();
            if (key.Length == 0) continue;
            env[key] = line[(idx + 1)..].Trim();
        }
        return env;
    }

    public static string SuppressList(ToolEntry tool)
    {
        var parts = new List<string>();
        if (tool.Tray != TrayMode.Native) parts.Add("tray");
        if (tool.Managed.SuppressKeepAwake) parts.Add("keepawake");
        if (tool.Managed.SuppressAutostart) parts.Add("autostart");
        return string.Join(',', parts);
    }
}
