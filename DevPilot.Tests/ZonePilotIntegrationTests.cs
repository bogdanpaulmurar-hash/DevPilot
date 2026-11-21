using DevPilot.Core.Hub;
using DevPilot.Core.Integrations;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Launch;
using DevPilot.Core.Presets;
using DevPilot.Infrastructure.Services;
using DevPilot.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevPilot.Tests;

public class ZonePilotIntegrationTests
{
    [Fact]
    public void Preset_matches_the_manifest_and_the_contract()
    {
        var p = ToolPresets.ZonePilot;
        Assert.Contains(p, ToolPresets.All);
        Assert.Equal(ToolKind.Executable, p.Kind);
        Assert.Equal("start", p.DefaultArguments);
        Assert.Equal("start --autostart", p.AutoStartArguments);
        Assert.Equal("--no-tray", p.NoTrayArguments);
        Assert.Equal("ZonePilot", p.ProcessMatch);
        Assert.Equal(@"%LOCALAPPDATA%\Programs\ZonePilot\ZonePilot.exe", p.DefaultPath);
        Assert.True(p.SupportsSuppressEnv);
        Assert.True(p.HasOwnTray);
        Assert.False(p.HasOwnKeepAwake);
        Assert.Contains("ZonePilot", p.StartupNames);

        var args = p.Actions().Where(a => a.Kind == ActionKind.Arguments).Select(a => a.Arguments).ToList();
        foreach (var expected in new[] { "editor", "settings", "apply columns-2", "apply rows-2", "apply grid-2x2", "apply grid-3x2", "apply columns-3", "apply focus", "reload", "stop" })
            Assert.Contains(expected, args);
        var configure = Assert.Single(p.Actions(), a => a.Kind == ActionKind.Page);
        Assert.Equal(ToolPresets.ZonePilotPage, configure.Value);
        Assert.Contains(p.Actions(), a => a.Kind == ActionKind.File && a.Value == @"{AppData}\ZonePilot\settings.json");
        Assert.Contains(p.Actions(), a => a.Kind == ActionKind.Folder && a.Value == @"{LocalAppData}\ZonePilot\logs");
    }

    [Fact]
    public void Created_tool_points_at_the_install_folder_and_is_managed()
    {
        var t = ToolPresets.Create(ToolPresets.ZonePilot, groupId: "g1");
        Assert.Equal(@"%LOCALAPPDATA%\Programs\ZonePilot\ZonePilot.exe", t.Path);
        Assert.Equal("g1", t.GroupId);
        Assert.Equal("zonepilot", t.Preset);
        Assert.Equal(TrayMode.HubOnly, t.Tray);
        Assert.True(t.Managed.Enabled);
        Assert.Equal("tray,keepawake,autostart", LaunchPlanner.SuppressList(t));

        var main = LaunchPlanner.ForTool(t);
        Assert.Equal(Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Programs\ZonePilot\ZonePilot.exe"), main.FileName);
        Assert.Equal("start --no-tray", main.Arguments);
        Assert.Equal("1", main.Environment[LaunchPlanner.EnvManaged]);
        Assert.Equal("start --autostart --no-tray", LaunchPlanner.ForTool(t, LaunchReason.AutoStart).Arguments);

        t.Tray = TrayMode.Native; // "keep the tool's own tray icon"
        Assert.Equal("start", LaunchPlanner.ForTool(t).Arguments);

        var apply = t.Actions.First(a => a.Arguments == "apply focus");
        Assert.Equal("apply focus", LaunchPlanner.ForAction(t, apply).Arguments);

        // A picked file wins over the default location.
        Assert.Equal(@"D:\Apps\ZonePilot.exe", ToolPresets.Create(ToolPresets.ZonePilot, @"D:\Apps\ZonePilot.exe").Path);
        Assert.Equal("zonepilot", ToolPresets.Recognise(new ToolEntry { Path = @"C:\x\ZonePilot.exe" })?.Key);
    }

    [Fact]
    public void Hub_top_bar_shows_pinned_tools_in_hub_order_at_most_two()
    {
        var s = new AppSettings();
        var g1 = new ToolGroup { Title = "A", Order = 0 };
        var g2 = new ToolGroup { Title = "B", Order = 1 };
        var hidden = new ToolGroup { Title = "H", Order = 2, Hidden = true };
        s.Groups.AddRange([g1, g2, hidden]);

        var zp = ToolPresets.Create(ToolPresets.ZonePilot, groupId: g2.Id);
        Assert.True(zp.PinToHubHeader); // the preset pins ZonePilot
        Assert.False(ToolPresets.Create(ToolPresets.RegTester, @"C:\r\regtester.py").PinToHubHeader);

        var a = new ToolEntry { Name = "a", GroupId = g1.Id, Order = 1, PinToHubHeader = true };
        var b = new ToolEntry { Name = "b", GroupId = g1.Id, Order = 0, PinToHubHeader = true };
        var notPinned = new ToolEntry { Name = "n", GroupId = g1.Id };
        var hiddenTool = new ToolEntry { Name = "h", GroupId = g1.Id, Hidden = true, PinToHubHeader = true };
        var inHiddenGroup = new ToolEntry { Name = "x", GroupId = hidden.Id, PinToHubHeader = true };
        var sep = new ToolEntry { Kind = ToolKind.Separator, GroupId = g1.Id, PinToHubHeader = true };
        s.Tools.AddRange([zp, a, b, notPinned, hiddenTool, inHiddenGroup, sep]);

        Assert.Equal(new[] { "b", "a" }, HubHeader.Pinned(s).Select(t => t.Name));
        b.PinToHubHeader = false;
        Assert.Equal(new[] { "a", "ZonePilot" }, HubHeader.Pinned(s).Select(t => t.Name));
    }

    [Fact]
    public void Process_match_finds_the_exe_only()
    {
        var t = ToolPresets.Create(ToolPresets.ZonePilot);
        Assert.True(ProcessMatcher.Matches(t, new ProcessInfo(1, "ZonePilot.exe", "\"C:\\Z\\ZonePilot.exe\" start")));
        Assert.False(ProcessMatcher.Matches(t, new ProcessInfo(2, "DevPilot.exe", "DevPilot.exe")));
    }

    [Fact]
    public async Task Page_action_opens_a_devpilot_page_instead_of_a_process()
    {
        var pages = new FakePages();
        var launcher = new Launcher(NullLogger<Launcher>.Instance, new FakeMonitor(), pages: pages);
        var t = ToolPresets.Create(ToolPresets.ZonePilot);
        var configure = t.Actions.First(a => a.Kind == ActionKind.Page);

        var ok = await launcher.RunActionAsync(t, configure);
        Assert.True(ok.Success);
        Assert.Equal(("zonepilot", t.Id), pages.Opened.Single());

        var unknown = await launcher.RunActionAsync(t, new ToolAction { Kind = ActionKind.Page, Value = "nope" });
        Assert.False(unknown.Success);
        var noOpener = await new Launcher(NullLogger<Launcher>.Instance, new FakeMonitor()).RunActionAsync(t, configure);
        Assert.False(noOpener.Success);
        Assert.Throws<InvalidOperationException>(() => LaunchPlanner.ForAction(t, configure));
        Assert.Equal(Glyphs.Settings, Glyphs.ForAction(ActionKind.Page));
    }

    private const string Sample = """
        {
          // ZonePilot writes camelCase; comments and unknown keys must survive
          "version": 1,
          "language": "ro",
          "defaultLayoutId": "grid-2x2",
          "monitorLayouts": { "GSM5B7F#5&2A7C": "focus" },
          "customLayouts": [ { "id": "custom-ab12cd34", "name": "Wide left", "root": { "split": "columns", "children": [ { "weight": 2 }, { "weight": 1 } ] } } ],
          "drag": { "enabled": true, "modifier": "Ctrl", "allowSpanning": false },
          "hotkeys": { "enabled": true, "editor": "Win+Shift+Z" },
          "appearance": { "accent": "#3D7EFF", "spacing": 12 },
          "excludedProcesses": [ "mstsc" ],
          "futureKey": { "x": 1 }
        }
        """;

    [Fact]
    public void Settings_edit_only_the_shown_fields_and_keep_the_rest()
    {
        var s = ZonePilotSettings.Parse(Sample);
        Assert.Equal("grid-2x2", s.DefaultLayoutId);
        Assert.Equal("Ctrl", s.DragModifier);
        Assert.True(s.DragEnabled);
        Assert.Equal("Win+Shift+Z", s.GetHotkey("editor"));
        Assert.Equal("Ctrl+Alt+Win+Left", s.GetHotkey("moveLeft")); // missing → ZonePilot's default
        Assert.Equal("Ctrl+Alt+Win", s.SnapToZoneModifiers);
        Assert.Equal("focus", s.MonitorLayoutFor("gsm5b7f#5&2a7c"));
        Assert.Contains(s.Layouts(), l => l.Id == "custom-ab12cd34" && l.Name == "Wide left" && !l.BuiltIn);
        Assert.Equal(7, s.Layouts().Count);

        s.DefaultLayoutId = "columns-3";
        s.DragModifier = "alt";
        s.DragEnabled = false;
        s.HotkeysEnabled = false;
        s.SetHotkey("moveUp", "Win+Alt+Up");
        s.SnapToZoneModifiers = "";

        var round = ZonePilotSettings.Parse(s.ToJson());
        Assert.Equal("columns-3", round.DefaultLayoutId);
        Assert.Equal("Alt", round.DragModifier);
        Assert.False(round.DragEnabled);
        Assert.False(round.HotkeysEnabled);
        Assert.Equal("Win+Alt+Up", round.GetHotkey("moveUp"));
        Assert.Equal("", round.SnapToZoneModifiers);

        var json = s.ToJson();
        Assert.Contains("\"allowSpanning\": false", json);
        Assert.Contains("\"futureKey\"", json);
        Assert.Contains("\"mstsc\"", json);
        Assert.Contains("\"language\": \"ro\"", json);
        Assert.Contains("\"spacing\": 12", json);
        Assert.Contains("Wide left", json);
    }

    [Fact]
    public void Missing_file_gives_defaults_and_save_is_atomic()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dp-zp-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        try
        {
            var s = ZonePilotSettings.Load(path);
            Assert.Equal("columns-2", s.DefaultLayoutId);
            Assert.Equal("Shift", s.DragModifier);
            Assert.Equal(6, s.Layouts().Count);
            s.DragModifier = "None";
            s.Save(path);
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".devpilot.tmp"));
            Assert.Equal("None", ZonePilotSettings.Load(path).DragModifier);
            Assert.ThrowsAny<System.Text.Json.JsonException>(() => ZonePilotSettings.Parse("{ broken"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("Win+Shift+Z", true)]
    [InlineData("Ctrl+Alt+Win+Left", true)]
    [InlineData("ctrl + alt + numpad5", true)]
    [InlineData("F9", true)]
    [InlineData("", true)]
    [InlineData("Z", false)]
    [InlineData("Win+Shift", false)]
    [InlineData("Ctrl+A+B", false)]
    [InlineData("Ctrl+Banana", false)]
    public void Hotkeys_follow_zonepilot_rules(string text, bool valid)
    {
        Assert.Equal(valid, ZonePilotSettings.IsValidHotkey(text, out var error));
        Assert.Equal(valid, error.Length == 0);
    }

    [Fact]
    public void Snap_modifiers_accept_only_modifiers()
    {
        Assert.True(ZonePilotSettings.IsValidModifiers("Ctrl+Alt+Win", out _));
        Assert.True(ZonePilotSettings.IsValidModifiers("", out _));
        Assert.False(ZonePilotSettings.IsValidModifiers("Ctrl+1", out _));
    }

    [Fact]
    public void Layout_list_from_the_cli_is_parsed()
    {
        const string output = """
            {
              "defaultLayout": "columns-2",
              "layouts": [
                { "id": "columns-2", "name": "Left / right", "zones": 2, "builtIn": true },
                { "id": "custom-1", "name": "Mine", "zones": 3, "builtIn": false }
              ],
              "monitors": [
                { "index": 2, "key": "B", "device": "\\\\.\\DISPLAY2", "primary": false, "bounds": "1920,0,2560x1440", "scale": 1.25, "layout": "custom-1" },
                { "index": 1, "key": "A", "device": "\\\\.\\DISPLAY1", "primary": true, "bounds": "0,0,1920x1080", "scale": 1, "layout": "columns-2" }
              ]
            }
            """;
        var list = ZonePilotSettings.ParseLayouts(output)!;
        Assert.Equal("columns-2", list.DefaultLayoutId);
        Assert.Equal(2, list.Layouts.Count);
        Assert.Equal(new[] { 1, 2 }, list.Monitors.Select(m => m.Index));
        Assert.True(list.Monitors[0].Primary);
        Assert.Equal(1.25, list.Monitors[1].Scale);
        Assert.Contains("Monitor 2", list.Monitors[1].Describe());
        Assert.Null(ZonePilotSettings.ParseLayouts("ZonePilot is not running."));
        Assert.Null(ZonePilotSettings.ParseLayouts(""));
    }

    [Fact]
    public void Monitor_changes_use_apply_and_clear_only_what_changed()
    {
        var s = ZonePilotSettings.Parse("""{ "monitorLayouts": { "A": "focus" } }""");
        var monitors = new[]
        {
            new ZonePilotMonitor(1, "A", "", 1, true, "focus"),
            new ZonePilotMonitor(2, "B", "", 1, false, "columns-2"),
            new ZonePilotMonitor(3, "C", "", 1, false, "columns-2"),
        };
        // 1 unchanged, 2 gets its own layout, 3 stays on the default.
        var plan = s.PlanMonitorChanges(monitors, new Dictionary<int, string> { [1] = "focus", [2] = "grid 3x2", [3] = "" });
        Assert.Equal(new[] { "apply \"grid 3x2\" --monitor 2" }, plan.ApplyArguments);
        Assert.Empty(plan.ClearKeys);

        // 1 goes back to the default layout: its entry is removed from the file.
        plan = s.PlanMonitorChanges(monitors, new Dictionary<int, string> { [1] = "" });
        Assert.Equal(new[] { "A" }, plan.ClearKeys);
        Assert.True(s.ClearMonitorLayout("a"));
        Assert.Equal("", s.MonitorLayoutFor("A"));
        Assert.Equal("apply focus --monitor 4", ZonePilotSettings.ApplyArguments("focus", 4));
    }

    private sealed class FakePages : IPageOpener
    {
        public List<(string Page, string ToolId)> Opened { get; } = [];
        public bool Open(string page, ToolEntry tool)
        {
            if (page != ToolPresets.ZonePilotPage) return false;
            Opened.Add((page, tool.Id));
            return true;
        }
    }

    private sealed class FakeMonitor : IProcessMonitor
    {
        public event EventHandler? StatusChanged { add { } remove { } }
        public event EventHandler<ToolEntry>? AutoRestartRequested { add { } remove { } }
        public ToolStatus Get(string toolId) => ToolStatus.Unknown(toolId);
        public IReadOnlyList<int> FindProcesses(ToolEntry tool) => [];
        public HealthStatus GetHealth(string targetId) => new(targetId, null, string.Empty);
        public void NotifyLaunched(string toolId, int processId) { }
        public void NotifyLaunchFailed(string toolId, string error) { }
        public void NotifyStopped(string toolId) { }
        public Task RefreshNowAsync() => Task.CompletedTask;
        public void Start() { }
        public void Dispose() { }
    }
}
