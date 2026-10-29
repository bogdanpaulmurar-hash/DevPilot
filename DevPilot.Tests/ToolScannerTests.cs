using DevPilot.Core.Guard;
using Xunit;

namespace DevPilot.Tests;

public class ToolScannerTests
{
    private static GuardReport ScanFile(string name, string content, LocalPolicy? policy = null)
    {
        using var tmp = new TempDir();
        File.WriteAllText(System.IO.Path.Combine(tmp.Path, name), content);
        return ToolScanner.Scan(tmp.Path, policy);
    }

    [Theory]
    [InlineData("a.cs", "var c = new HttpClient();", "net-http")]
    [InlineData("a.py", "r = requests.post(url)", "net-http")]
    [InlineData("a.cs", "var t = new TcpClient();", "net-socket")]
    [InlineData("a.py", "import smtplib", "net-ftp-smtp")]
    [InlineData("a.cs", "var u = \"https://api.vendor.test/v1\";", "hardcoded-url")]
    [InlineData("a.py", "HOST = '10.20.30.40'", "hardcoded-ip")]
    [InlineData("a.py", "import sentry_sdk  # telemetry", "telemetry")]
    [InlineData("a.cs", "AutoUpdater.Start();", "auto-update")]
    [InlineData("a.cs", "client.UploadFile(path);", "upload")]
    [InlineData("a.py", "import pyperclip", "capture-clipboard")]
    [InlineData("a.py", "img = ImageGrab.grab()", "capture-screen")]
    [InlineData("a.cs", "SetWindowsHookEx(WH_KEYBOARD_LL, cb, h, 0);", "capture-keys")]
    [InlineData("a.py", "subprocess.run(['x'])", "spawn-process")]
    [InlineData("a.ps1", "schtasks /create /tn x", "persistence")]
    [InlineData("a.py", "import openai", "ai-client")]
    [InlineData("a.cs", "var k = \"AKIAABCDEFGHIJKLMNOP\";", "secret")]
    [InlineData("a.py", "password = \"hunter2hunter2\"", "secret")]
    public void Default_rules_find(string file, string code, string ruleId) =>
        Assert.Contains(ScanFile(file, code).Findings, f => f.RuleId == ruleId);

    [Theory]
    [InlineData("var x = 1;")]
    [InlineData("var u = \"http://localhost:5000/health\";")]
    [InlineData("var ip = \"127.0.0.1\";")]
    [InlineData("var s = \"https://example.com/docs\";")]
    public void Harmless_code_has_no_findings(string code) =>
        Assert.Empty(ScanFile("a.cs", code).Findings);

    [Fact]
    public void Finding_has_file_line_and_help_but_not_the_matched_text()
    {
        var report = ScanFile("sub.py", "x = 1\npassword = \"hunter2hunter2\"\n");
        var f = Assert.Single(report.Findings, f => f.RuleId == "secret");
        Assert.Equal("sub.py", f.File);
        Assert.Equal(2, f.Line);
        Assert.Equal(GuardSeverity.High, f.Severity);
        Assert.NotEmpty(f.Why);
        Assert.NotEmpty(f.Fix);
        Assert.DoesNotContain("hunter2", f.Message + f.Why + f.Fix + f.File);
    }

    [Fact]
    public void Skips_build_and_dependency_folders_and_non_text_files()
    {
        using var tmp = new TempDir();
        foreach (var dir in new[] { "bin", "obj", "node_modules", ".git" })
        {
            Directory.CreateDirectory(System.IO.Path.Combine(tmp.Path, dir));
            File.WriteAllText(System.IO.Path.Combine(tmp.Path, dir, "x.cs"), "new HttpClient();");
        }
        File.WriteAllText(System.IO.Path.Combine(tmp.Path, "notes.txt"), "new HttpClient();");
        Assert.Empty(ToolScanner.Scan(tmp.Path).Findings);
    }

    [Fact]
    public void Scans_subfolders_and_reports_relative_paths()
    {
        using var tmp = new TempDir();
        var sub = System.IO.Path.Combine(tmp.Path, "src", "net");
        Directory.CreateDirectory(sub);
        File.WriteAllText(System.IO.Path.Combine(sub, "c.cs"), "new HttpClient();");
        var f = Assert.Single(ToolScanner.Scan(tmp.Path).Findings);
        Assert.Equal(System.IO.Path.Combine("src", "net", "c.cs"), f.File);
    }

    [Fact]
    public void Caps_findings_per_rule_and_file()
    {
        var code = string.Join('\n', Enumerable.Repeat("new HttpClient();", 20));
        Assert.Equal(ToolScanner.MaxFindingsPerRulePerFile, ScanFile("a.cs", code).Findings.Count(f => f.RuleId == "net-http"));
    }

    [Fact]
    public void Local_deny_terms_are_flagged_without_revealing_them()
    {
        var policy = new LocalPolicy { DenyTerms = ["Project-X"] };
        var report = ScanFile("a.py", "# notes about project-x\n", policy);
        var f = Assert.Single(report.Findings, f => f.RuleId == ToolScanner.DenyTermRuleId);
        Assert.Equal(GuardSeverity.High, f.Severity);
        Assert.DoesNotContain("project-x", f.Message + f.Why + f.Fix, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(ScanFile("a.py", "# notes about project-x\n").Findings.Where(x => x.RuleId == ToolScanner.DenyTermRuleId));
    }

    [Fact]
    public void Missing_folder_gives_empty_report() =>
        Assert.Empty(ToolScanner.Scan(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "devpilot-nope-" + Guid.NewGuid().ToString("N"))).Findings);

    [Fact]
    public void Custom_rules_replace_the_defaults_and_broken_patterns_are_skipped()
    {
        var rules = GuardRuleSet.FromJson("""
            [ { "id": "mine", "severity": "Info", "pattern": "forbidden", "message": "m" },
              { "id": "broken", "pattern": "([", "message": "x" } ]
            """);
        Assert.Single(rules.Compiled);
        using var tmp = new TempDir();
        File.WriteAllText(System.IO.Path.Combine(tmp.Path, "a.py"), "forbidden\nnew HttpClient();");
        var findings = ToolScanner.Scan(tmp.Path, rules: rules).Findings;
        Assert.Equal("mine", Assert.Single(findings).RuleId);
    }

    [Fact]
    public void All_default_rules_compile_and_are_documented()
    {
        var rules = GuardRuleSet.Default.Compiled;
        Assert.True(rules.Count >= 15);
        foreach (var (rule, _) in rules)
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Message));
            Assert.False(string.IsNullOrWhiteSpace(rule.Why));
            Assert.False(string.IsNullOrWhiteSpace(rule.Fix));
        }
    }
}
