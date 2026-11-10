using DevPilot.Core.Repos;
using Xunit;

namespace DevPilot.Tests;

public sealed class LocalDrivesTests
{
    private static DriveCandidate Drive(string root, long gb = 100) => new(root, gb * 1024L * 1024 * 1024);

    [Fact]
    public void Offers_C_then_D_then_the_rest_and_leaves_out_the_drive_of_the_cloud_copy()
    {
        if (!OperatingSystem.IsWindows()) return;   // drive letters only exist on Windows
        var choices = LocalDrives.Choices("ScreenPilot", @"G:\My Drive\Dev\ScreenPilot",
            [Drive(@"G:\"), Drive(@"E:\"), Drive(@"D:\"), Drive(@"C:\")], _ => true);
        Assert.Equal(new[] { @"C:\", @"D:\", @"E:\" }, choices.Select(c => c.Root));
        Assert.Equal(@"C:\Dev\ScreenPilot", choices[0].Path);
        Assert.Equal("C:", choices[0].Letter);
        Assert.Equal("100 GB free", choices[0].FreeText);
    }

    [Fact]
    public void Another_laptop_with_only_one_drive_gets_that_drive()
    {
        if (!OperatingSystem.IsWindows()) return;
        var choices = LocalDrives.Choices("NexusX", @"C:\Users\x\Drive\NexusX", [Drive(@"C:\")], _ => true);
        Assert.Empty(choices);   // the only drive holds the cloud copy: nothing else to offer
        var two = LocalDrives.Choices("NexusX", @"C:\Users\x\Drive\NexusX", [Drive(@"C:\"), Drive(@"F:\")], _ => true);
        Assert.Equal(new[] { @"F:\Dev\NexusX" }, two.Select(c => c.Path));
    }

    [Fact]
    public void A_drive_where_the_clone_folder_is_taken_is_not_offered()
    {
        if (!OperatingSystem.IsWindows()) return;
        var choices = LocalDrives.Choices("App", @"G:\Dev\App", [Drive(@"C:\"), Drive(@"D:\")], p => !p.StartsWith("C:"));
        Assert.Equal(new[] { @"D:\Dev\App" }, choices.Select(c => c.Path));
    }

    [Fact]
    public void Detect_never_throws_and_returns_only_rooted_drives()
    {
        var found = LocalDrives.Detect();
        Assert.All(found, d => Assert.False(string.IsNullOrWhiteSpace(d.Root)));
    }
}
