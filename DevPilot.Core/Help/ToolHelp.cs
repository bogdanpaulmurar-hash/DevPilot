using DevPilot.Models;

namespace DevPilot.Core.Help;

/// <summary>One explained setting of the tool editor, with copy-ready examples.</summary>
public sealed record HelpTopic(string Title, string Text, IReadOnlyList<string> Examples)
{
    public bool HasExamples => Examples.Count > 0;
    public string ExamplesText => string.Join(Environment.NewLine, Examples);
}

/// <summary>
/// Explanations and examples for the settings of a tool entry, shown in the editor.
/// Kept in Core so the catalogue is testable and every kind is guaranteed to have help.
/// </summary>
public static class ToolHelp
{
    private static HelpTopic T(string title, string text, params string[] examples) => new(title, text, examples);

    /// <summary>Help for the "What it starts" fields of one kind, most important first.</summary>
    public static IReadOnlyList<HelpTopic> ForKind(ToolKind kind) => kind switch
    {
        ToolKind.Executable =>
        [
            T("Program", "Full path of the .exe. Browse… fills it in. Names that Windows can find (notepad.exe) also work.",
                @"C:\Program Files\7-Zip\7zFM.exe", "notepad.exe"),
            T("Arguments", "Everything you would type after the program name. Wrap a value with spaces in double quotes.",
                @"--profile ""Work""", @"""{Documents}\notes.txt"""),
            T("Working folder", "Where the program starts. Empty = the folder of the program. Set it when the program reads files relative to its own folder."),
        ],
        ToolKind.Python =>
        [
            T("Script or module", "Use either a script file or a module, not both. A module (python -m name) is the usual choice for tools installed with pip or in a venv.",
                @"Script: C:\Dev\mytool\main.pyw", "Module: mytool"),
            T("Interpreter", "Empty = pythonw.exe from PATH (no console window). For a tool installed in its own virtual environment, point to that venv's pythonw.exe.",
                @"%LOCALAPPDATA%\MyTool\venv\Scripts\pythonw.exe", @"C:\Dev\mytool\.venv\Scripts\pythonw.exe"),
            T("Arguments", "Passed to the script or module, e.g. the sub-command or a flag that starts it minimised.",
                "gui", "gui --minimized"),
            T("Working folder", @"Empty = the script's folder. For a module, set the project folder if it reads relative paths such as .\config."),
        ],
        ToolKind.PowerShell =>
        [
            T("Script", "Path of the .ps1 file. DevPilot starts it with -NoProfile and -ExecutionPolicy Bypass for this run only, so a blocked policy does not stop it.",
                @"C:\Dev\scripts\backup.ps1"),
            T("Arguments", "Parameters of the script.", "-Target D:\\Backup -Verbose"),
        ],
        ToolKind.Batch =>
        [
            T("Batch file", "Path of the .bat or .cmd file.", @"C:\Dev\scripts\build.cmd"),
            T("Arguments", "Passed as %1, %2… to the batch file.", "Release"),
            T("No console window", "Tick it for scripts that do not need to show output; leave it off when you want to see errors."),
        ],
        ToolKind.VBScript =>
        [
            T("Script", "Path of the .vbs file; it runs with wscript (no console).", @"C:\Dev\scripts\cleanup.vbs"),
        ],
        ToolKind.ShellCommand =>
        [
            T("Command line", "Any command exactly as you would type it in a terminal (runs through cmd.exe). The window stays open after it ends, unless No console window is ticked.",
                "ipconfig /flushdns", @"git -C C:\Dev\myrepo pull"),
            T("Working folder", "Folder the command runs in; useful for git or build commands.", @"C:\Dev\myrepo"),
        ],
        ToolKind.Folder =>
        [
            T("Folder", "Opens in File Explorer. Tick Folder content in the hub (further down) to browse it from the hub on hover.",
                @"{Documents}\Projects", @"G:\My Drive\Dev"),
        ],
        ToolKind.NetworkShare =>
        [
            T("Network folder", @"UNC path (\\server\share) or a mapped drive; opens in File Explorer.",
                @"\\nas\team\reports", @"Z:\shared"),
        ],
        ToolKind.Url =>
        [
            T("Address", "Opens in the default browser. Use http://127.0.0.1:port/ for tools with a local web page.",
                "https://github.com/", "http://127.0.0.1:8765/"),
        ],
        ToolKind.Document =>
        [
            T("File", "Opens with the program Windows associates with the file type.",
                @"{Documents}\checklist.xlsx", @"C:\Dev\notes\todo.md"),
        ],
        ToolKind.Ssh =>
        [
            T("Connection", "user@host, opened in a terminal window with ssh. Add -p for another port in Arguments.", "admin@192.168.1.10"),
            T("Arguments", "Extra ssh options.", "-p 2222", "-i {Home}\\.ssh\\id_ed25519"),
        ],
        ToolKind.Wsl =>
        [
            T("Command", "Passed to wsl.exe, in a terminal window. Without -d it runs in the default distribution.",
                "htop", "-d Ubuntu -- docker compose up"),
        ],
        _ => [],
    };

    /// <summary>Help for the sections below "What it starts", the same for every kind.</summary>
    public static IReadOnlyList<HelpTopic> Common { get; } =
    [
        T("Tokens", "These words are replaced when the tool starts, so a configuration works on any PC: {ToolDir} (folder of the tool), {Home}, {Pictures}, {Documents}, {Desktop}, {AppData}, {LocalAppData} and %VARIABLES%.",
            @"{ToolDir}\config.json", @"{Pictures}\ScreenPilot", @"%USERPROFILE%\Desktop"),
        T("Options shown on hover", "Each option is a row in the flyout. Run the tool with arguments = starts the same program with other arguments (e.g. a command that is forwarded to the running tool). Run a command = any other program. Open a web page / folder / file = opens it. Featured options become the big buttons of the spotlight card.",
            "Arguments: action capture.region", "Command: notepad.exe  +  {ToolDir}\\log.txt", "Web page: http://127.0.0.1:8765/", @"Folder: {LocalAppData}\MyTool"),
        T("Recognise the process by", "How DevPilot knows the tool is running (green dot, Stop, Restart). Empty = automatic from the program or script name. Use a distinctive word from the command line; several patterns are separated with ';'.",
            "screenpilot", "mytool.pyw;mytool.exe"),
        T("Health URL", "Optional. For a tool with a local web page: when DevPilot cannot recognise its process, it shows the tool as running while this address answers.", "http://127.0.0.1:8765/"),
        T("Start with DevPilot", "Replaces the tool's own Windows autostart. Delay spreads the starts so the PC stays responsive; Order decides who goes first. \"Arguments for the automatic start\" are used only for this start.",
            "--minimized", "gui --autostart"),
        T("Extra arguments when its tray is suppressed", "Added only when DevPilot owns the tray icon, for tools that need a flag to hide their own icon.", "--no-tray"),
        T("Extra environment", "One KEY=VALUE per line, passed to the tool at start.", "MYTOOL_PROFILE=work", "PYTHONUTF8=1"),
        T("Tray icon", "Only in the hub = one icon for everything (recommended). Separate icon = its own icon with this tool's options. Keep its own = DevPilot does not touch it."),
    ];
}
