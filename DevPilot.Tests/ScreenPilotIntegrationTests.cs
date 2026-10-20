using DevPilot.Core.Config;
using DevPilot.Core.Presets;
using DevPilot.Infrastructure.Services;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

public class ScreenPilotIntegrationTests
{
    private static List<ToolAction> LegacyActions() =>
    [
        new() { Label = "Window", Kind = ActionKind.Arguments, Arguments = "capture window", Featured = true },
        new() { Label = "Screen", Kind = ActionKind.Arguments, Arguments = "capture current" },
        new() { Label = "Record 10 s", Kind = ActionKind.Arguments, Arguments = "record --seconds 10" },
        new() { Label = "Captures folder", Kind = ActionKind.Folder, Value = @"%USERPROFILE%\Pictures\ScreenPilot" },
    ];

    [Fact]
    public void Preset_options_run_inside_the_running_app()
    {
        var actions = ToolPresets.ScreenPilot.Actions();
        Assert.Contains(actions, a => a.Arguments == "action capture.region" && a.Featured);
        Assert.Contains(actions, a => a.Arguments == "action app.open_history");
        Assert.All(actions.Where(a => a.Kind == ActionKind.Arguments),
            a => Assert.True(a.Arguments.StartsWith("action ") || a.Arguments.StartsWith("diagnostics "), a.Arguments));
        Assert.NotEmpty(ToolPresets.ScreenPilot.Interpreters!);
    }

    [Fact]
    public void Untouched_2_0_options_are_upgraded_once()
    {
        var tool = ToolPresets.Create(ToolPresets.ScreenPilot, @"C:\src\ScreenPilot");
        tool.Actions = LegacyActions();
        Assert.True(ToolPresets.UpgradeLegacyActions(tool));
        Assert.Equal("action capture.region", tool.Actions[0].Arguments);
        Assert.False(ToolPresets.UpgradeLegacyActions(tool)); // idempotent
    }

    [Fact]
    public void Customised_options_are_kept()
    {
        var tool = ToolPresets.Create(ToolPresets.ScreenPilot, @"C:\src\ScreenPilot");
        tool.Actions = LegacyActions();
        tool.Actions.Add(new ToolAction { Label = "Mine", Kind = ActionKind.Arguments, Arguments = "capture region --rect 0,0,10,10" });
        Assert.False(ToolPresets.UpgradeLegacyActions(tool));
        Assert.Equal("capture window", tool.Actions[0].Arguments);

        var other = new ToolEntry { Name = "Other", Kind = ToolKind.Python, PythonModule = "other", Actions = LegacyActions() };
        Assert.False(ToolPresets.UpgradeLegacyActions(other));
    }

    [Fact]
    public void Loading_a_config_upgrades_the_options()
    {
        var s = new AppSettings();
        var tool = ToolPresets.Create(ToolPresets.ScreenPilot, @"C:\src\ScreenPilot");
        tool.Actions = LegacyActions();
        s.Tools.Add(tool);
        Normalizer.Normalize(s);
        Assert.Equal("action capture.region", s.Tools[0].Actions[0].Arguments);
    }

    [Fact]
    public void Early_exit_message_explains_a_missing_module()
    {
        var message = Launcher.DescribeEarlyExit(1,
        [
            "Traceback (most recent call last):",
            "File \"<frozen runpy>\", line 189, in _run_module_as_main",
            "ModuleNotFoundError: No module named 'screenpilot'",
        ]);
        Assert.Contains("No module named 'screenpilot'", message);
        Assert.Contains("Interpreter", message);
        Assert.Contains("exit code 3", Launcher.DescribeEarlyExit(3, []));
    }
}
