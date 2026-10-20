using DevPilot.Core.Launch;
using DevPilot.Core.Presets;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

public class LaunchPlannerTests
{
    [Fact]
    public void Executable_gets_managed_environment()
    {
        var t = new ToolEntry { Id = "t1", Kind = ToolKind.Executable, Path = @"C:\Apps\My App\app.exe", Arguments = "--x" };
        var p = LaunchPlanner.ForTool(t);
        Assert.Equal(LaunchMode.Process, p.Mode);
        Assert.Equal(@"C:\Apps\My App\app.exe", p.FileName);
        Assert.Equal("--x", p.Arguments);
        Assert.Equal(@"C:\Apps\My App", p.WorkingDirectory);
        Assert.Equal("1", p.Environment[LaunchPlanner.EnvManaged]);
        Assert.Equal("tray,keepawake,autostart", p.Environment[LaunchPlanner.EnvSuppress]);
        Assert.Equal("t1", p.Environment[LaunchPlanner.EnvToolId]);
        Assert.True(p.IsMainProcess);
    }

    [Fact]
    public void Native_tray_is_not_suppressed_and_unmanaged_has_no_env()
    {
        var t = new ToolEntry { Kind = ToolKind.Executable, Path = "a.exe", Tray = TrayMode.Native };
        t.Managed.SuppressKeepAwake = false;
        Assert.Equal("autostart", LaunchPlanner.SuppressList(t));
        t.Managed.Enabled = false;
        Assert.DoesNotContain(LaunchPlanner.EnvManaged, LaunchPlanner.ForTool(t).Environment.Keys);
    }

    [Fact]
    public void Python_script_and_module()
    {
        var script = new ToolEntry { Kind = ToolKind.Python, Path = @"C:\src\Focus Pilot\FocusPilot.pyw", HideWindow = true };
        var p1 = LaunchPlanner.ForTool(script);
        Assert.Equal("pythonw.exe", p1.FileName);
        Assert.Equal("\"C:\\src\\Focus Pilot\\FocusPilot.pyw\"", p1.Arguments);

        var module = ToolPresets.Create(ToolPresets.ScreenPilot, @"C:\src\ScreenPilot");
        Assert.Equal("screenpilot", module.PythonModule);
        Assert.Equal(@"C:\src\ScreenPilot", module.WorkingDirectory);
        var p2 = LaunchPlanner.ForTool(module);
        Assert.Equal("-m screenpilot gui", p2.Arguments);
        Assert.Equal(@"C:\src\ScreenPilot", p2.WorkingDirectory);

        var auto = LaunchPlanner.ForTool(module, LaunchReason.AutoStart);
        Assert.Equal("-m screenpilot gui --autostart", auto.Arguments);

        module.Interpreter = @"C:\venv\Scripts\pythonw.exe";
        Assert.Equal(@"C:\venv\Scripts\pythonw.exe", LaunchPlanner.ForTool(module).FileName);
    }

    [Fact]
    public void No_tray_arguments_only_for_main_launch()
    {
        var t = ToolPresets.Create(ToolPresets.RegTester, @"C:\src\regTester\regtester.py");
        Assert.Equal(TrayMode.HubOnly, t.Tray);
        Assert.EndsWith("--no-tray", LaunchPlanner.ForTool(t).Arguments);

        var action = new ToolAction { Kind = ActionKind.Arguments, Arguments = "--port 9000" };
        Assert.DoesNotContain("--no-tray", LaunchPlanner.ForAction(t, action).Arguments);

        t.Tray = TrayMode.Native;
        Assert.DoesNotContain("--no-tray", LaunchPlanner.ForTool(t).Arguments);
    }

    [Fact]
    public void Actions_expand_tokens()
    {
        var t = new ToolEntry { Kind = ToolKind.Python, Path = @"C:\src\fp\FocusPilot.pyw" };
        var file = LaunchPlanner.ForAction(t, new ToolAction { Kind = ActionKind.File, Value = @"{ToolDir}\config.json" });
        Assert.Equal(LaunchMode.ShellOpen, file.Mode);
        Assert.Equal(@"C:\src\fp\config.json", file.FileName);

        var folder = LaunchPlanner.ForAction(t, new ToolAction { Kind = ActionKind.Folder, Value = @"C:\My Files" });
        Assert.Equal("explorer.exe", folder.FileName);
        Assert.Equal("\"C:\\My Files\"", folder.Arguments);

        var url = LaunchPlanner.ForAction(t, new ToolAction { Kind = ActionKind.Url, Value = "http://127.0.0.1:8765/#/specs" });
        Assert.Equal("http://127.0.0.1:8765/#/specs", url.FileName);

        var cmd = LaunchPlanner.ForAction(t, new ToolAction { Kind = ActionKind.Command, Value = "notepad.exe", Arguments = "{ToolDir}\\a.txt" });
        Assert.Equal(LaunchMode.Process, cmd.Mode);
        Assert.Equal(@"C:\src\fp\a.txt", cmd.Arguments);

        Assert.Throws<InvalidOperationException>(() => LaunchPlanner.ForAction(t, new ToolAction { Kind = ActionKind.Separator }));
    }

    [Fact]
    public void Shell_kinds_and_admin()
    {
        Assert.Equal(LaunchMode.ShellOpen, LaunchPlanner.ForTool(new ToolEntry { Kind = ToolKind.Url, Path = "https://x" }).Mode);
        var folder = LaunchPlanner.ForTool(new ToolEntry { Kind = ToolKind.Folder, Path = @"C:\a b" });
        Assert.Equal("explorer.exe", folder.FileName);
        var admin = LaunchPlanner.ForTool(new ToolEntry { Kind = ToolKind.Executable, Path = "a.exe", RunAsAdmin = true });
        Assert.Equal(LaunchMode.ShellOpen, admin.Mode);
        Assert.True(admin.RunAsAdmin);
        var bat = LaunchPlanner.ForTool(new ToolEntry { Kind = ToolKind.Batch, Path = @"C:\x y\run.bat", Arguments = "1" });
        Assert.Equal("cmd.exe", bat.FileName);
        Assert.Equal("/c \"\"C:\\x y\\run.bat\" 1\"", bat.Arguments);
    }

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("", "\"\"")]
    [InlineData("\"already quoted\"", "\"already quoted\"")]
    [InlineData("a \"q\"", "\"a \\\"q\\\"\"")]
    public void Quote(string input, string expected) => Assert.Equal(expected, CommandLineText.Quote(input));

    [Fact]
    public void Extra_environment_lines()
    {
        var t = new ToolEntry { Kind = ToolKind.Executable, Path = "a.exe" };
        t.Managed.ExtraEnvironment = "FOO=bar\nbad\n BAZ = 1 \n";
        var env = LaunchPlanner.ManagedEnvironment(t);
        Assert.Equal("bar", env["FOO"]);
        Assert.Equal("1", env["BAZ"]);
    }
}
