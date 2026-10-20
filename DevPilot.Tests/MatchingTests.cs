using DevPilot.Core.Hub;
using DevPilot.Core.KeepAwake;
using DevPilot.Core.Launch;
using DevPilot.Core.Presets;
using DevPilot.Core.Startup;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

public class MatchingTests
{
    [Fact]
    public void Process_matcher_for_exe_and_python()
    {
        var exe = new ToolEntry { Kind = ToolKind.Executable, Path = @"C:\a\AutoPilot.exe" };
        Assert.True(ProcessMatcher.Matches(exe, new ProcessInfo(1, "AutoPilot.exe", "")));
        Assert.False(ProcessMatcher.Matches(exe, new ProcessInfo(1, "notepad.exe", "")));

        var py = new ToolEntry { Kind = ToolKind.Python, Path = @"C:\src\FocusPilot.pyw" };
        Assert.True(ProcessMatcher.Matches(py, new ProcessInfo(2, "pythonw.exe", @"pythonw.exe C:\src\FocusPilot.pyw")));
        Assert.False(ProcessMatcher.Matches(py, new ProcessInfo(3, "pythonw.exe", @"pythonw.exe C:\src\other.py")));
        // A non-python process with the script name in its command line is not the tool.
        Assert.False(ProcessMatcher.Matches(py, new ProcessInfo(4, "code.exe", @"code.exe C:\src\FocusPilot.pyw")));

        var mod = ToolPresets.Create(ToolPresets.ScreenPilot, @"C:\src\ScreenPilot");
        Assert.True(ProcessMatcher.Matches(mod, new ProcessInfo(5, "pythonw.exe", "pythonw.exe -m screenpilot gui")));

        var url = new ToolEntry { Kind = ToolKind.Url, Path = "http://x" };
        Assert.False(ProcessMatcher.CanTrack(url));
    }

    [Fact]
    public void Startup_matcher_finds_known_and_path_entries()
    {
        var sp = ToolPresets.Create(ToolPresets.ScreenPilot, @"C:\src\ScreenPilot");
        var exe = new ToolEntry { Name = "Helper", Kind = ToolKind.Executable, Path = @"C:\Tools\helper.exe" };
        var entries = new[]
        {
            new StartupEntry("HKCU-Run", "ScreenPilot", "pythonw -m screenpilot gui --autostart"),
            new StartupEntry("HKCU-Run", "SomethingElse", "\"C:\\Tools\\helper.exe\" /min"),
            new StartupEntry("HKCU-Run", "OneDrive", "onedrive.exe"),
            new StartupEntry("HKCU-Run", "DevPilot", "devpilot.exe"),
        };
        var conflicts = StartupMatcher.FindConflicts(entries, [sp, exe]);
        Assert.Equal(2, conflicts.Count);
        Assert.Contains(conflicts, c => c.Entry.Name == "ScreenPilot" && c.Tool == sp);
        Assert.Contains(conflicts, c => c.Entry.Name == "SomethingElse" && c.Tool == exe);
    }

    [Fact]
    public void Keep_awake_schedule()
    {
        var s = new KeepAwakeSettings { Enabled = true, ScheduleEnabled = true, ScheduleFrom = "08:00", ScheduleTo = "18:00", ScheduleDays = [1, 2, 3, 4, 5] };
        Assert.True(KeepAwakeSchedule.IsActive(s, new DateTime(2026, 10, 19, 9, 0, 0)));   // Monday
        Assert.False(KeepAwakeSchedule.IsActive(s, new DateTime(2026, 10, 19, 19, 0, 0)));
        Assert.False(KeepAwakeSchedule.IsActive(s, new DateTime(2026, 10, 18, 9, 0, 0)));  // Sunday

        s.ScheduleFrom = "22:00"; s.ScheduleTo = "06:00";
        Assert.True(KeepAwakeSchedule.IsActive(s, new DateTime(2026, 10, 19, 23, 0, 0)));
        Assert.True(KeepAwakeSchedule.IsActive(s, new DateTime(2026, 10, 20, 2, 0, 0)));   // Tue night belongs to Monday
        Assert.False(KeepAwakeSchedule.IsActive(s, new DateTime(2026, 10, 19, 2, 0, 0)));  // Monday 02:00 → Sunday's night

        s.Enabled = false;
        Assert.False(KeepAwakeSchedule.IsActive(s, DateTime.Now));
        s.Enabled = true; s.ScheduleEnabled = false;
        Assert.True(KeepAwakeSchedule.IsActive(s, DateTime.Now));
    }

    [Fact]
    public void Hub_search_ignores_case_and_diacritics()
    {
        var t = new ToolEntry { Name = "Rapoarte lunare", Description = "Situație", Actions = [new ToolAction { Label = "Deschide" }] };
        Assert.True(HubSearch.Matches(t, "Birou", "situatie"));
        Assert.True(HubSearch.Matches(t, "Birou", "rap desch"));
        Assert.True(HubSearch.Matches(t, "Birou", "birou"));
        Assert.False(HubSearch.Matches(t, "Birou", "excel"));
        Assert.True(HubSearch.Matches(t, null, "  "));
    }

    [Theory]
    [InlineData("E722", "\uE722")]
    [InlineData("U+E722", "\uE722")]
    [InlineData("\\uE722", "\uE722")]
    [InlineData("★", "★")]
    [InlineData("AB", "AB")]
    [InlineData("", "")]
    public void Glyph_parse(string input, string expected) => Assert.Equal(expected, Glyphs.Parse(input));

    [Fact]
    public void Presets_are_recognised()
    {
        Assert.Equal("regtester", ToolPresets.Recognise(new ToolEntry { Path = @"D:\x\regTester\regtester.py" })?.Key);
        Assert.Equal("focuspilot", ToolPresets.Recognise(new ToolEntry { Name = "Focus Pilot" })?.Key);
        Assert.Null(ToolPresets.Recognise(new ToolEntry { Name = "Calculator", Path = "calc.exe" }));
        var fp = ToolPresets.Create(ToolPresets.FocusPilot, @"C:\src\FocusPilot\FocusPilot.pyw");
        Assert.Equal(@"C:\src\FocusPilot\FocusPilot.pyw", fp.Path);
        Assert.Equal(string.Empty, fp.PythonModule);
        Assert.Equal(ToolKind.Python, fp.Kind);
    }
}
