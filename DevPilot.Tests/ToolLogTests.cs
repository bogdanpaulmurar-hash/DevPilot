using DevPilot.Core.Launch;
using DevPilot.Core.Logging;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

public class ToolLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dp-toollog-" + Guid.NewGuid().ToString("N"));
    public ToolLogTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void FileName_is_safe_and_contains_the_id()
    {
        var tool = new ToolEntry { Id = "abc123", Name = "My: tool/<1>" };
        var name = ToolLogFiles.FileName(tool);
        Assert.EndsWith("-abc123.log", name);
        Assert.DoesNotContain(name, c => Path.GetInvalidFileNameChars().Contains(c));
        Assert.Equal("tool-x.log", ToolLogFiles.FileName(new ToolEntry { Id = "x", Name = "  " }));
    }

    [Fact]
    public void Rotation_keeps_the_requested_number_of_files()
    {
        var path = Path.Combine(_dir, "t-1.log");
        for (int i = 1; i <= 5; i++)
        {
            File.WriteAllText(path, new string('x', 200) + i);
            ToolLogFiles.RotateIfNeeded(path, 100, keep: 3);
        }
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".1"));
        Assert.True(File.Exists(path + ".2"));
        Assert.False(File.Exists(path + ".3"));
        Assert.EndsWith("5", File.ReadAllText(path + ".1"));
        Assert.EndsWith("4", File.ReadAllText(path + ".2"));
    }

    [Fact]
    public void Small_files_are_not_rotated()
    {
        var path = Path.Combine(_dir, "t-2.log");
        File.WriteAllText(path, "small");
        ToolLogFiles.RotateIfNeeded(path, 1000, 3);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".1"));
    }

    [Fact]
    public void Tail_returns_only_the_last_lines()
    {
        var path = Path.Combine(_dir, "t-3.log");
        File.WriteAllLines(path, Enumerable.Range(1, 500).Select(i => "line " + i));
        var lines = ToolLogFiles.Tail(path, 3).Split(Environment.NewLine);
        Assert.Equal(new[] { "line 498", "line 499", "line 500" }, lines);
        Assert.Equal(string.Empty, ToolLogFiles.Tail(Path.Combine(_dir, "missing.log"), 5));
    }

    [Fact]
    public void Orphans_are_logs_of_tools_that_no_longer_exist()
    {
        var keep = new ToolEntry { Id = "k1", Name = "Keep" };
        var files = new[] { Path.Combine(_dir, "Keep-k1.log"), Path.Combine(_dir, "Keep-k1.log.2"), Path.Combine(_dir, "Gone-g9.log"), Path.Combine(_dir, "Gone-g9.log.1"), Path.Combine(_dir, "notes.txt") };
        var orphans = ToolLogFiles.Orphans(files, [keep]).Select(Path.GetFileName).ToList();
        Assert.Equal(["Gone-g9.log", "Gone-g9.log.1"], orphans);
    }

    [Fact]
    public void Start_block_has_command_reason_and_devpilot_variables()
    {
        var tool = new ToolEntry { Id = "a", Name = "Demo", Kind = ToolKind.Python, Path = @"C:\t\app.py", Arguments = "--x 1", HideWindow = true };
        var plan = LaunchPlanner.ForTool(tool, LaunchReason.AutoStart);
        var text = ToolLogFormat.StartBlock(tool, LaunchReason.AutoStart, plan);
        Assert.Contains("START Demo (AutoStart)", text);
        Assert.Contains("command :", text);
        Assert.Contains("DEVPILOT_MANAGED=1", text);
    }

    [Fact]
    public void Exit_line_shows_code_and_duration()
    {
        Assert.Equal("EXIT code 0 after 2.0s", ToolLogFormat.ExitLine(0, TimeSpan.FromSeconds(2)));
        Assert.Contains("code 3 after 1m 5s (non-zero", ToolLogFormat.ExitLine(3, TimeSpan.FromSeconds(65)));
        Assert.Equal("STARTED pid 42", ToolLogFormat.StartResult(true, 42, null));
        Assert.Equal("FAILED boom", ToolLogFormat.StartResult(false, 0, "boom"));
    }
}
