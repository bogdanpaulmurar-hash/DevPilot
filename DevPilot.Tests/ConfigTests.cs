using DevPilot.Core.Config;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

public class ConfigTests
{
    private const string V1Json = """
    {
      "theme": "light",
      "minimize_to_tray": false,
      "jiggler_enabled": true,
      "jiggler_interval_sec": 45,
      "jiggler_method": "Key Press",
      "heartbeat_targets": [ { "name": "Portal", "target": "http://localhost:1/", "type": "Url", "is_enabled": true } ],
      "data_cleansing_exe_path": "D:\\Work\\Starter.exe",
      "test_client_exe_path": "",
      "apps": [
        { "id": "a1", "name": "Notes", "path": "C:\\Notes", "type": "Folder", "category": "Docs", "sort_order": 2, "tag_color": "ff0000", "tray_font_style": "Bold" },
        { "id": "a2", "name": "Shot", "path": "C:\\src\\ScreenPilot\\run.pyw", "type": "Python", "sort_order": 1, "is_visible": false },
        { "id": "a1", "name": "Dup id", "path": "calc.exe", "type": "Executable" }
      ],
      "custom_tools": [ { "id": "c1", "name": "Helper", "exe_path": "C:\\Tools\\helper.exe" } ],
      "hotkeys": [ { "Id": 1, "Action": "ShowHide", "Modifiers": "Ctrl+Shift", "Key": "D", "IsEnabled": true },
                   { "Id": 3, "Action": "Screenshot", "Modifiers": "Ctrl+Shift", "Key": "S", "IsEnabled": true } ]
    }
    """;

    [Fact]
    public void Detects_schema_versions()
    {
        Assert.Equal(1, ConfigSerializer.DetectSchema(V1Json));
        Assert.Equal(2, ConfigSerializer.DetectSchema(ConfigSerializer.Serialize(Defaults.Create())));
        Assert.Equal(0, ConfigSerializer.DetectSchema("{}"));
    }

    [Fact]
    public void Migrates_v1_without_losing_entries()
    {
        var s = ConfigSerializer.Load(V1Json, out var migrated);
        Assert.True(migrated);
        Assert.Equal(ThemeMode.Light, s.Theme);
        Assert.False(s.Window.CloseToTray);
        Assert.True(s.KeepAwake.Enabled);
        Assert.Equal(KeepAwakeMode.F15Key, s.KeepAwake.Mode);
        Assert.Equal(45, s.KeepAwake.IntervalSeconds);

        // 3 apps + 1 hard-coded path + 1 custom tool
        Assert.Equal(5, s.Tools.Count);
        Assert.Equal(s.Tools.Count, s.Tools.Select(t => t.Id).Distinct().Count());

        var notes = s.Tools.Single(t => t.Name == "Notes");
        Assert.Equal(ToolKind.Folder, notes.Kind);
        Assert.Equal("#FF0000", notes.Color);
        Assert.True(notes.Bold);
        Assert.Equal("Docs", s.Groups.Single(g => g.Id == notes.GroupId).Title);

        var shot = s.Tools.Single(t => t.Name == "Shot");
        Assert.Equal("screenpilot", shot.Preset);
        Assert.True(shot.Hidden);
        Assert.NotEmpty(shot.Actions);

        Assert.Contains(s.Tools, t => t.Path == "D:\\Work\\Starter.exe");
        Assert.Contains(s.Tools, t => t.Name == "Helper");
        Assert.Single(s.Monitor.ExtraTargets);
        Assert.True(s.Monitor.ExtraTargets[0].IsUrl);

        // ShowHide migrated, removed Screenshot action dropped
        Assert.Single(s.Hotkeys);
        Assert.Equal("main", s.Hotkeys[0].Action);
        Assert.All(s.Tools, t => Assert.Contains(s.Groups, g => g.Id == t.GroupId));
    }

    [Fact]
    public void V2_round_trip_keeps_values()
    {
        var s = Defaults.Create();
        s.Accent = "#112233";
        s.Hub.Width = 420;
        s.Hub.Spotlight.Accent = "#abcdef";
        s.Tools.Add(new ToolEntry
        {
            Name = "X", GroupId = s.Groups[0].Id, Kind = ToolKind.Python, PythonModule = "x", Tray = TrayMode.DevPilotIcon,
            Actions = [new ToolAction { Label = "Go", Kind = ActionKind.Url, Value = "http://a" }],
        });
        var json = ConfigSerializer.Serialize(s);
        Assert.Contains("\"tray\": \"DevPilotIcon\"", json);
        var back = ConfigSerializer.Load(json, out var migrated);
        Assert.False(migrated);
        Assert.Equal("#112233", back.Accent);
        Assert.Equal(420, back.Hub.Width);
        Assert.Equal("#ABCDEF", back.Hub.Spotlight.Accent);
        var t = Assert.Single(back.Tools);
        Assert.Equal(TrayMode.DevPilotIcon, t.Tray);
        Assert.Equal("Go", t.Actions[0].Label);
    }

    [Fact]
    public void Normalizer_repairs_groups_ids_and_ranges()
    {
        var s = new AppSettings();
        s.Tools.Add(new ToolEntry { Id = "same", Name = "A", GroupId = "missing" });
        s.Tools.Add(new ToolEntry { Id = "same", Name = "B" });
        s.Hub.Width = 5000;
        s.Hub.Opacity = 0.1;
        s.KeepAwake.IntervalSeconds = 1;
        s.Accent = "nonsense";
        Normalizer.Normalize(s);
        Assert.Single(s.Groups);
        Assert.All(s.Tools, t => Assert.Equal(s.Groups[0].Id, t.GroupId));
        Assert.NotEqual(s.Tools[0].Id, s.Tools[1].Id);
        Assert.Equal(900, s.Hub.Width);
        Assert.Equal(0.7, s.Hub.Opacity);
        Assert.Equal(10, s.KeepAwake.IntervalSeconds);
        Assert.Equal("#4F8EF7", s.Accent);
    }

    [Fact]
    public void Ordering_moves_tools_and_groups()
    {
        var s = Defaults.Create();
        var g1 = s.Groups[0]; var g2 = s.Groups[1];
        var a = new ToolEntry { Name = "a", GroupId = g1.Id, Order = 0 };
        var b = new ToolEntry { Name = "b", GroupId = g1.Id, Order = 1 };
        var c = new ToolEntry { Name = "c", GroupId = g2.Id, Order = 0 };
        s.Tools.AddRange([a, b, c]);

        Assert.True(Ordering.MoveTool(s, b, -1));
        Assert.Equal(["b", "a"], Ordering.ToolsOf(s, g1.Id).Select(t => t.Name));
        Assert.False(Ordering.MoveTool(s, b, -1));

        Ordering.MoveToGroup(s, a, g2.Id, 0);
        Assert.Equal(["a", "c"], Ordering.ToolsOf(s, g2.Id).Select(t => t.Name));
        Assert.Equal(["b"], Ordering.ToolsOf(s, g1.Id).Select(t => t.Name));

        Assert.True(Ordering.MoveGroup(s, g2, -1));
        Assert.Equal(g2.Id, s.Groups.OrderBy(g => g.Order).First().Id);
    }
}
