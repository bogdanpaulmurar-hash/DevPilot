using DevPilot.Core.Cloud;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

public class CloudTests
{
    private sealed class FakeProbe : ICloudProbe
    {
        public HashSet<string> Processes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool Mounted { get; set; } = true;
        public bool Reachable { get; set; } = true;
        public int Checks { get; private set; }
        public bool IsProcessRunning(string n) { Checks++; return Processes.Contains(n); }
        public bool IsRootMounted(string r) => Mounted;
        public bool IsReachable(string p) => Reachable;
    }

    private static ToolEntry Tool(string path, CloudProvider provider = CloudProvider.Auto, int timeout = 10,
        CloudTimeoutAction onTimeout = CloudTimeoutAction.Skip) => new()
    {
        Name = "T", Kind = ToolKind.Executable, Path = path,
        AutoStart = { Enabled = true, Mode = AutoStartMode.WaitForCloud, CloudProvider = provider, CloudTimeoutSeconds = timeout, OnTimeout = onTimeout },
    };

    [Theory]
    [InlineData(@"C:\Users\x\OneDrive\tools\a.exe", CloudProvider.OneDrive)]
    [InlineData(@"C:\Users\x\OneDrive - Contoso\a.exe", CloudProvider.OneDrive)]
    [InlineData(@"G:\My Drive\tools\a.exe", CloudProvider.GoogleDrive)]
    [InlineData(@"H:\Shared drives\team\a.exe", CloudProvider.GoogleDrive)]
    [InlineData(@"C:\Program Files\a.exe", CloudProvider.Auto)]
    [InlineData("", CloudProvider.Auto)]
    public void Provider_is_detected_from_the_path(string path, CloudProvider expected) =>
        Assert.Equal(expected, CloudReadiness.Resolve(CloudProvider.Auto, path));

    [Fact]
    public void Explicit_provider_wins_and_onedrive_roots_are_used()
    {
        Assert.Equal(CloudProvider.GoogleDrive, CloudReadiness.Resolve(CloudProvider.GoogleDrive, @"C:\x\OneDrive\a.exe"));
        Assert.Equal(CloudProvider.OneDrive, CloudReadiness.Resolve(CloudProvider.Auto, @"D:\Cloud\a.exe", [@"D:\Cloud"]));
    }

    [Fact]
    public void Ready_needs_process_mount_and_reachable_path()
    {
        var p = new FakeProbe();
        Assert.False(CloudReadiness.IsReady(CloudProvider.GoogleDrive, @"G:\My Drive\a", p));
        p.Processes.Add("GoogleDriveFS");
        Assert.True(CloudReadiness.IsReady(CloudProvider.GoogleDrive, @"G:\My Drive\a", p));
        p.Mounted = false;
        Assert.False(CloudReadiness.IsReady(CloudProvider.GoogleDrive, @"G:\My Drive\a", p));
        p.Mounted = true; p.Reachable = false;
        Assert.False(CloudReadiness.IsReady(CloudProvider.GoogleDrive, @"G:\My Drive\a", p));
    }

    private static CloudGate Gate(FakeProbe p, Action? onDelay = null) =>
        new(p, null, (_, _) => { onDelay?.Invoke(); return Task.CompletedTask; });

    [Fact]
    public async Task Delay_mode_and_non_cloud_paths_do_not_wait()
    {
        var p = new FakeProbe();
        var delayTool = Tool(@"G:\My Drive\a.exe"); delayTool.AutoStart.Mode = AutoStartMode.Delay;
        Assert.Equal(GateOutcome.Proceed, await Gate(p).WaitAsync(delayTool));
        Assert.Equal(GateOutcome.Proceed, await Gate(p).WaitAsync(Tool(@"C:\Tools\a.exe")));
        Assert.Equal(0, p.Checks);
    }

    [Fact]
    public async Task Waits_until_the_drive_becomes_ready()
    {
        var p = new FakeProbe();
        int ticks = 0;
        var gate = Gate(p, () => { if (++ticks == 3) p.Processes.Add("GoogleDriveFS"); });
        Assert.Equal(GateOutcome.Proceed, await gate.WaitAsync(Tool(@"G:\My Drive\a.exe")));
        Assert.Equal(3, ticks);
    }

    [Fact]
    public async Task Timeout_skips_or_starts_anyway()
    {
        var p = new FakeProbe();
        var statuses = new List<string?>();
        Assert.Equal(GateOutcome.TimedOutSkip, await Gate(p).WaitAsync(Tool(@"G:\My Drive\a.exe", timeout: 6), statuses.Add));
        Assert.Contains(statuses, s => s is not null && s.Contains("Google Drive"));
        Assert.Null(statuses[^1]);
        Assert.Equal(GateOutcome.Proceed, await Gate(p).WaitAsync(Tool(@"G:\My Drive\a.exe", timeout: 6, onTimeout: CloudTimeoutAction.StartAnyway)));
    }

    [Fact]
    public async Task Tools_on_the_same_drive_share_the_wait()
    {
        var p = new FakeProbe();
        int ticks = 0;
        var gate = Gate(p, () => ticks++);
        Assert.Equal(GateOutcome.TimedOutSkip, await gate.WaitAsync(Tool(@"G:\My Drive\a.exe", timeout: 6)));
        var first = ticks;
        Assert.Equal(GateOutcome.TimedOutSkip, await gate.WaitAsync(Tool(@"G:\My Drive\b.exe", timeout: 6)));
        Assert.Equal(first, ticks); // no second wait

        p.Processes.Add("GoogleDriveFS");
        var gate2 = Gate(p);
        Assert.Equal(GateOutcome.Proceed, await gate2.WaitAsync(Tool(@"G:\My Drive\a.exe")));
        var checks = p.Checks;
        Assert.Equal(GateOutcome.Proceed, await gate2.WaitAsync(Tool(@"G:\My Drive\b.exe")));
        Assert.Equal(checks, p.Checks); // ready is remembered
    }

    [Fact]
    public void Old_configs_keep_delay_mode()
    {
        var o = new AutoStartOptions();
        Assert.Equal(AutoStartMode.Delay, o.Mode);
        Assert.Equal(120, o.CloudTimeoutSeconds);
        Assert.Equal(CloudTimeoutAction.Skip, o.OnTimeout);
    }
}
