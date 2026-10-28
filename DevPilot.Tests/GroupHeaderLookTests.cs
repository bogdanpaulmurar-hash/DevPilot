using DevPilot.Core.Backup;
using DevPilot.Core.Config;
using DevPilot.Core.Hub;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

public class GroupHeaderLookTests
{
    [Fact]
    public void Default_size_follows_the_hub_header_style_and_larger_sizes_follow_the_font()
    {
        Assert.Equal(GroupHeaderLook.SmallCapsSize, GroupHeaderLook.TitleSize(GroupTitleSize.Default, GroupHeaderStyle.SmallCaps, 13));
        Assert.Equal(13, GroupHeaderLook.TitleSize(GroupTitleSize.Default, GroupHeaderStyle.Normal, 13));
        Assert.Equal(16, GroupHeaderLook.TitleSize(GroupTitleSize.Large, GroupHeaderStyle.SmallCaps, 13));
        Assert.Equal(20, GroupHeaderLook.TitleSize(GroupTitleSize.ExtraLarge, GroupHeaderStyle.Normal, 13));
        Assert.True(GroupHeaderLook.LogoSize(GroupHeaderLook.SmallCapsSize) >= 14);
        Assert.Equal(25, GroupHeaderLook.LogoSize(20));
    }

    [Fact]
    public void Weight_bold_wins_then_semibold_then_the_hub_style()
    {
        var g = new ToolGroup();
        Assert.Equal(400, GroupHeaderLook.TitleWeight(g, GroupHeaderStyle.Normal));
        Assert.Equal(700, GroupHeaderLook.TitleWeight(g, GroupHeaderStyle.SmallCaps));
        g.TitleFont = GroupTitleFont.Semibold;
        Assert.Equal(600, GroupHeaderLook.TitleWeight(g, GroupHeaderStyle.Normal));
        g.Bold = true;
        Assert.Equal(700, GroupHeaderLook.TitleWeight(g, GroupHeaderStyle.Normal));
    }

    [Fact]
    public void Only_serif_and_mono_change_the_font_family()
    {
        Assert.Equal(string.Empty, GroupHeaderLook.FontFamily(GroupTitleFont.Default));
        Assert.Equal(string.Empty, GroupHeaderLook.FontFamily(GroupTitleFont.Semibold));
        Assert.StartsWith("Georgia", GroupHeaderLook.FontFamily(GroupTitleFont.Serif));
        Assert.Contains("Consolas", GroupHeaderLook.FontFamily(GroupTitleFont.Mono));
    }

    [Fact]
    public void Logo_carrier_resolves_only_the_picked_file()
    {
        Assert.Null(GroupHeaderLook.LogoCarrier(new ToolGroup()));
        Assert.Null(GroupHeaderLook.LogoCarrier(new ToolGroup { IconPath = "   " }));

        var carrier = GroupHeaderLook.LogoCarrier(new ToolGroup { Id = "g1", Title = "Work", IconPath = @" C:\L\work.png " });
        Assert.NotNull(carrier);
        Assert.Equal("group:g1", carrier!.Id);
        var logo = LogoSource.Resolve(carrier, manifestIcon: null);
        Assert.Equal(LogoOrigin.File, logo.Origin);
        Assert.Equal(@"C:\L\work.png", logo.Path);
    }

    [Fact]
    public void Group_logos_are_backed_up_restored_and_adapted()
    {
        var s = new AppSettings();
        s.Groups.Add(new ToolGroup { Id = "g1", Title = "Work", IconPath = @"C:\Users\bob\L\work.png" });
        s.Groups.Add(new ToolGroup { Id = "g2", Title = "Home", IconPath = @"C:\Apps\tool.exe" });
        s.Tools.Add(new ToolEntry { Name = "A", GroupId = "g1", IconPath = @"C:\Users\bob\L\work.png" });

        var plan = BackupPlanner.PlanLogos(s, _ => true);
        var logo = Assert.Single(plan);
        Assert.Equal(@"C:\Users\bob\L\work.png", logo.Source);

        var map = new Dictionary<string, string> { [@"C:\Users\bob\L\work.png"] = @"C:\Users\alice\AppData\Roaming\DevPilot\logos\work.png" };
        var merged = BackupRestore.Merge(new AppSettings(), s, map, null, null, false, out _);
        Assert.Equal(@"C:\Users\alice\AppData\Roaming\DevPilot\logos\work.png", merged.Groups.Single(g => g.Id == "g1").IconPath);

        var copy = BackupRestore.Merge(new AppSettings(), s, null, @"C:\Users\bob", @"C:\Users\alice", true, out int adapted);
        Assert.Equal(@"C:\Users\alice\L\work.png", copy.Groups.Single(g => g.Id == "g1").IconPath);
        Assert.Equal(2, adapted); // the group logo and the tool logo
    }

    [Fact]
    public void Group_look_survives_a_save_and_bad_values_are_repaired()
    {
        var s = new AppSettings();
        s.Groups.Add(new ToolGroup
        {
            Id = "g1", Title = "Work", IconPath = @"C:\L\work.png", TitleAlign = GroupTitleAlign.Center,
            TitleSize = GroupTitleSize.ExtraLarge, TitleFont = GroupTitleFont.Serif, Bold = true, Italic = true,
        });
        var loaded = ConfigSerializer.Load(ConfigSerializer.Serialize(s), out _);
        var g = Assert.Single(loaded.Groups);
        Assert.Equal(@"C:\L\work.png", g.IconPath);
        Assert.Equal(GroupTitleAlign.Center, g.TitleAlign);
        Assert.Equal(GroupTitleSize.ExtraLarge, g.TitleSize);
        Assert.Equal(GroupTitleFont.Serif, g.TitleFont);
        Assert.True(g.Bold);
        Assert.True(g.Italic);

        g.IconPath = null!;
        g.TitleAlign = (GroupTitleAlign)42;
        g.TitleSize = (GroupTitleSize)42;
        g.TitleFont = (GroupTitleFont)42;
        Normalizer.Normalize(loaded);
        Assert.Equal(string.Empty, g.IconPath);
        Assert.Equal(GroupTitleAlign.Left, g.TitleAlign);
        Assert.Equal(GroupTitleSize.Default, g.TitleSize);
        Assert.Equal(GroupTitleFont.Default, g.TitleFont);
    }
}
