using DevPilot.Core.Config;
using DevPilot.Core.Hub;
using DevPilot.Core.Presets;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

public class HubLabelTests
{
    [Theory]
    [InlineData(HubLabelMode.Default, HubLabelMode.TextAndSymbol, HubLabelMode.TextAndSymbol)]
    [InlineData(HubLabelMode.Default, HubLabelMode.Auto, HubLabelMode.Auto)]
    [InlineData(HubLabelMode.Default, HubLabelMode.Default, HubLabelMode.TextAndSymbol)]
    [InlineData(HubLabelMode.TextOnly, HubLabelMode.Auto, HubLabelMode.TextOnly)]
    [InlineData((HubLabelMode)99, HubLabelMode.LogoOnly, HubLabelMode.LogoOnly)]
    public void Entry_mode_or_hub_mode(HubLabelMode own, HubLabelMode hub, HubLabelMode expected) =>
        Assert.Equal(expected, HubLabel.Effective(own, hub));

    [Theory]
    // mode, has logo → logo, symbol, name
    [InlineData(HubLabelMode.TextAndSymbol, true, false, true, true)]
    [InlineData(HubLabelMode.TextAndSymbol, false, false, true, true)]
    [InlineData(HubLabelMode.TextOnly, true, false, false, true)]
    [InlineData(HubLabelMode.TextOnly, false, false, false, true)]
    [InlineData(HubLabelMode.Auto, true, true, false, true)]
    [InlineData(HubLabelMode.Auto, false, false, true, true)]
    [InlineData(HubLabelMode.LogoAndText, true, true, false, true)]
    [InlineData(HubLabelMode.LogoAndText, false, false, true, true)]
    [InlineData(HubLabelMode.LogoOnly, true, true, false, false)]
    [InlineData(HubLabelMode.LogoOnly, false, false, true, true)]
    public void Resolves_parts_and_falls_back_without_logo(HubLabelMode mode, bool hasLogo, bool logo, bool symbol, bool name) =>
        Assert.Equal(new LabelParts(logo, symbol, name), HubLabel.Resolve(mode, hasLogo));

    [Fact]
    public void Row_is_never_empty()
    {
        foreach (var mode in Enum.GetValues<HubLabelMode>())
        foreach (var hasLogo in new[] { true, false })
        {
            var p = HubLabel.Resolve(mode, HubLabelMode.TextAndSymbol, hasLogo);
            Assert.True(p.Logo || p.Name, $"{mode} / logo={hasLogo}");
        }
    }

    [Theory]
    [InlineData(HubLabelMode.Auto, true)]
    [InlineData(HubLabelMode.LogoAndText, true)]
    [InlineData(HubLabelMode.LogoOnly, true)]
    [InlineData(HubLabelMode.TextAndSymbol, false)]
    [InlineData(HubLabelMode.TextOnly, false)]
    public void Logo_is_only_loaded_when_it_can_show(HubLabelMode mode, bool wants) =>
        Assert.Equal(wants, HubLabel.WantsLogo(mode));

    [Fact]
    public void Old_config_keeps_the_classic_look()
    {
        var json = ConfigSerializer.Serialize(Defaults.Create());
        // A 2.2 config has no LabelMode anywhere.
        json = json.Replace("\"labelMode\"", "\"_removedLabelMode\"", StringComparison.OrdinalIgnoreCase);
        var s = ConfigSerializer.Load(json, out _);
        Assert.Equal(HubLabelMode.TextAndSymbol, s.Hub.LabelMode);
        Assert.All(s.Tools, t => Assert.Equal(HubLabelMode.Default, t.LabelMode));
        Assert.All(s.Tools, t => Assert.Equal(LabelParts.Classic, HubLabel.Resolve(t.LabelMode, s.Hub.LabelMode, hasLogo: true)));
    }

    [Fact]
    public void Hub_mode_default_is_repaired()
    {
        var s = Defaults.Create();
        s.Hub.LabelMode = HubLabelMode.Default;
        Normalizer.Normalize(s);
        Assert.Equal(HubLabelMode.TextAndSymbol, s.Hub.LabelMode);
    }
}

public class LogoSourceTests
{
    [Fact]
    public void Own_file_wins()
    {
        var tool = new ToolEntry { Kind = ToolKind.Executable, Path = @"C:\Apps\x.exe", IconPath = "\"C:\\Logos\\x.png\"" };
        Assert.Equal(new LogoRef(@"C:\Logos\x.png", LogoOrigin.File), LogoSource.Resolve(tool, @"C:\Apps\icon.png"));
    }

    [Fact]
    public void Manifest_icon_comes_before_the_program_icon()
    {
        var tool = new ToolEntry { Kind = ToolKind.Executable, Path = @"C:\Apps\x.exe" };
        Assert.Equal(new LogoRef(@"C:\Apps\icon.png", LogoOrigin.Manifest), LogoSource.Resolve(tool, @"C:\Apps\icon.png"));
        Assert.Equal(new LogoRef(@"C:\Apps\x.exe", LogoOrigin.Target), LogoSource.Resolve(tool, null));
    }

    [Theory]
    [InlineData(ToolKind.Executable, @"C:\Apps\x.exe", true)]
    [InlineData(ToolKind.Executable, @"C:\Apps\x.lnk", true)]
    [InlineData(ToolKind.Document, @"C:\Docs\plan.xlsx", true)]
    [InlineData(ToolKind.Python, @"C:\src\tool.pyw", false)]
    [InlineData(ToolKind.Batch, @"C:\src\run.bat", false)]
    [InlineData(ToolKind.Folder, @"C:\src", false)]
    [InlineData(ToolKind.Url, "https://example.com", false)]
    [InlineData(ToolKind.Executable, "", false)]
    public void Program_icon_only_where_it_means_something(ToolKind kind, string path, bool hasLogo)
    {
        var r = LogoSource.Resolve(new ToolEntry { Kind = kind, Path = path }, null);
        Assert.Equal(hasLogo, r.Origin == LogoOrigin.Target);
        Assert.Equal(!hasLogo, r.IsNone);
    }

    [Fact]
    public void Separators_have_no_logo() =>
        Assert.True(LogoSource.Resolve(new ToolEntry { Kind = ToolKind.Separator, IconPath = @"C:\a.png" }, null).IsNone);

    [Fact]
    public void Manifest_icon_is_relative_to_the_manifest()
    {
        Assert.Equal(@"C:\src\Tool\assets\logo.png", LogoSource.ManifestIconPath(@"C:\src\Tool\devpilot.tool.json", @"assets\logo.png"));
        Assert.Equal(@"C:\src\logo.png", LogoSource.ManifestIconPath(@"C:\src\Tool\devpilot.tool.json", @"..\logo.png"));
        Assert.Equal(@"D:\x.ico", LogoSource.ManifestIconPath(@"C:\src\Tool\devpilot.tool.json", @"D:\x.ico"));
        Assert.Equal(string.Empty, LogoSource.ManifestIconPath(@"C:\src\Tool\devpilot.tool.json", " "));
    }

    [Fact]
    public void Cache_key_changes_with_the_file()
    {
        var t = new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);
        var k = LogoSource.CacheKey(@"C:\a.png", 100, t);
        Assert.EndsWith(".png", k);
        Assert.Equal(k, LogoSource.CacheKey(@"c:\A.PNG", 100, t));
        Assert.NotEqual(k, LogoSource.CacheKey(@"C:\a.png", 101, t));
        Assert.NotEqual(k, LogoSource.CacheKey(@"C:\a.png", 100, t.AddSeconds(1)));
        Assert.NotEqual(k, LogoSource.CacheKey(@"C:\b.png", 100, t));
    }

    [Theory]
    [InlineData(256, 256, 64, 64)]
    [InlineData(32, 32, 64, 64)]
    [InlineData(200, 100, 64, 32)]
    [InlineData(50, 100, 32, 64)]
    public void Fits_into_a_square(int w, int h, int ew, int eh) =>
        Assert.Equal((ew, eh), LogoSource.FitInto(w, h));

    [Theory]
    [InlineData("a.PNG", true, false, false)]
    [InlineData("a.ico", false, true, false)]
    [InlineData("a.exe", false, false, true)]
    [InlineData("a.lnk", false, false, true)]
    [InlineData("a.txt", false, false, false)]
    public void Recognises_file_types(string path, bool image, bool ico, bool holder)
    {
        Assert.Equal(image, LogoSource.IsImage(path));
        Assert.Equal(ico, LogoSource.IsIcoFile(path));
        Assert.Equal(holder, LogoSource.IsIconHolder(path));
    }

    [Fact]
    public void Manifest_icon_is_read_and_applied()
    {
        using var tmp = new TempDir();
        var script = Path.Combine(tmp.Path, "mytool.pyw");
        File.WriteAllText(script, "");
        File.WriteAllText(Path.Combine(tmp.Path, ToolManifest.FileName), """{ "icon": "assets/logo.png" }""");
        var tool = new ToolEntry { Name = "Mine", Kind = ToolKind.Python, Path = script };
        var expected = Path.GetFullPath(Path.Combine(tmp.Path, "assets", "logo.png"));

        Assert.Equal(expected, ToolManifest.IconFor(tool));
        Assert.Equal(new LogoRef(expected, LogoOrigin.Manifest), LogoSource.Resolve(tool, ToolManifest.IconFor(tool)));

        ToolManifest.Load(ToolManifest.Find(tool)!).ApplyTo(tool);
        Assert.Equal(expected, tool.IconPath);
    }

    [Fact]
    public void Tool_without_manifest_has_no_manifest_icon() =>
        Assert.Equal(string.Empty, ToolManifest.IconFor(new ToolEntry { Kind = ToolKind.Python, Path = @"Z:\nowhere\none.pyw" }));
}
