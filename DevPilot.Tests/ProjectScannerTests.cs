using DevPilot.Core.Detection;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

public sealed class ProjectScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "devpilot-scan-" + Guid.NewGuid().ToString("N"));

    public ProjectScannerTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private string Project(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Touch(string dir, string relative, string content = "")
    {
        var path = Path.Combine(dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void Python_package_runs_as_module_from_src()
    {
        var dir = Project("mytool");
        Touch(dir, "pyproject.toml");
        Touch(dir, @"src\mytool\__main__.py");
        Touch(dir, @"src\mytool\__init__.py");
        var found = ProjectScanner.Scan(dir);
        Assert.Equal(ToolKind.Python, found.Kind);
        Assert.Equal("mytool", found.PythonModule);
        Assert.Equal(Path.Combine(dir, "src"), found.WorkingDirectory);
        Assert.True(found.HideWindow);
    }

    [Fact]
    public void Python_script_uses_the_project_venv()
    {
        var dir = Project("tray");
        Touch(dir, "requirements.txt");
        Touch(dir, "main.pyw");
        Touch(dir, @".venv\Scripts\pythonw.exe");
        var found = ProjectScanner.Scan(dir);
        Assert.Equal(ToolKind.Python, found.Kind);
        Assert.Equal(Path.Combine(dir, "main.pyw"), found.Path);
        Assert.Equal(Path.Combine(dir, ".venv", "Scripts", "pythonw.exe"), found.Interpreter);
        Assert.True(found.HideWindow);
    }

    [Fact]
    public void Console_python_script_keeps_its_window()
    {
        var dir = Project("cli");
        Touch(dir, "main.py");
        Assert.False(ProjectScanner.Scan(dir).HideWindow);
    }

    [Fact]
    public void Built_csharp_program_is_used()
    {
        var dir = Project("App");
        Touch(dir, @"App\App.csproj", "<Project><PropertyGroup><OutputType>WinExe</OutputType></PropertyGroup></Project>");
        Touch(dir, @"App\bin\Release\net8.0-windows\App.exe");
        var found = ProjectScanner.Scan(dir);
        Assert.Equal(ToolKind.Executable, found.Kind);
        Assert.EndsWith("App.exe", found.Path);
    }

    [Fact]
    public void Unbuilt_csharp_program_starts_with_dotnet_run()
    {
        var dir = Project("App");
        Touch(dir, @"App\App.csproj", "<Project><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>");
        var found = ProjectScanner.Scan(dir);
        Assert.Equal(ToolKind.ShellCommand, found.Kind);
        Assert.Equal("dotnet", found.Path);
        Assert.Contains("run -c Release --project", found.Arguments);
        Assert.Equal(dir, found.WorkingDirectory);
    }

    [Fact]
    public void Library_and_test_projects_are_ignored()
    {
        var dir = Project("Lib");
        Touch(dir, @"Lib\Lib.csproj", "<Project><PropertyGroup><OutputType>Library</OutputType></PropertyGroup></Project>");
        Touch(dir, @"Lib.Tests\Lib.Tests.csproj", "<Project><PropertyGroup><OutputType>Exe</OutputType><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
        Assert.Equal(ToolKind.Folder, ProjectScanner.Scan(dir).Kind);
    }

    [Fact]
    public void Node_app_with_start_script()
    {
        var dir = Project("web");
        Touch(dir, "package.json", "{\"scripts\":{\"start\":\"node index.js\"}}");
        var found = ProjectScanner.Scan(dir);
        Assert.Equal(ToolKind.ShellCommand, found.Kind);
        Assert.Equal("npm", found.Path);
        Assert.Equal("start", found.Arguments);
    }

    [Fact]
    public void Launcher_script_is_found()
    {
        var dir = Project("old");
        Touch(dir, "start.cmd");
        var found = ProjectScanner.Scan(dir);
        Assert.Equal(ToolKind.Batch, found.Kind);
        Assert.Equal(Path.Combine(dir, "start.cmd"), found.Path);
    }

    [Fact]
    public void Single_exe_in_the_folder_is_used()
    {
        var dir = Project("portable");
        Touch(dir, "thing.exe");
        var found = ProjectScanner.Scan(dir);
        Assert.Equal(ToolKind.Executable, found.Kind);
        Assert.Equal(Path.Combine(dir, "thing.exe"), found.Path);
    }

    [Fact]
    public void Unknown_folder_falls_back_to_a_folder_entry()
    {
        var dir = Project("notes");
        Touch(dir, "readme.txt");
        var found = ProjectScanner.Scan(dir);
        Assert.Equal(ToolKind.Folder, found.Kind);
        Assert.Equal(dir, found.Path);
    }

    [Fact]
    public void Missing_folder_does_not_throw()
    {
        var found = ProjectScanner.Scan(Path.Combine(_root, "nope"));
        Assert.Equal(ToolKind.Folder, found.Kind);
    }

    [Fact]
    public void Dependency_folders_are_not_scanned()
    {
        var dir = Project("web");
        Touch(dir, @"node_modules\dep\start.cmd");
        Assert.Equal(ToolKind.Folder, ProjectScanner.Scan(dir).Kind);
    }

    [Fact]
    public void Scan_is_bounded_on_huge_folders()
    {
        var dir = Project("big");
        for (int i = 0; i < ProjectScanner.MaxEntries + 200; i++) Touch(dir, $"f{i:D5}.txt");
        var found = ProjectScanner.Scan(dir);
        Assert.Equal(ToolKind.Folder, found.Kind);
    }

    [Fact]
    public void Manifest_fills_the_tool()
    {
        var dir = Project("Described");
        Touch(dir, "start.cmd");
        Touch(dir, "devpilot.tool.json", "{\"description\":\"From manifest\",\"actions\":[{\"label\":\"Open\",\"kind\":\"Url\",\"value\":\"http://127.0.0.1:1/\"}]}");
        var tool = ProjectScanner.Scan(dir).ToTool();
        Assert.Equal(ToolKind.Batch, tool.Kind);
        Assert.Single(tool.Actions);
    }

    [Fact]
    public void Known_tool_folder_gets_its_preset()
    {
        var dir = Project("ScreenPilot");
        Touch(dir, "pyproject.toml");
        Touch(dir, @"src\screenpilot\__main__.py");
        var tool = ProjectScanner.Scan(dir).ToTool();
        Assert.Equal("screenpilot", tool.Preset);
        Assert.NotEmpty(tool.Actions);
        Assert.Equal("screenpilot", tool.PythonModule);
    }
}
