using System.Text.RegularExpressions;

namespace DevPilot.Core.Guard;

/// <summary>
/// Static scan of a tool folder: reports what the code could do with the network and with local data.
/// Conservative and read-only: it only reads text files, never changes or deletes anything, and a finding never
/// contains the matched text (so secrets and deny-list terms do not end up in reports).
/// </summary>
public static class ToolScanner
{
    public const int MaxFiles = 3000;
    public const int MaxFileBytes = 512 * 1024;
    public const int MaxFindingsPerRulePerFile = 3;
    private const int MaxDepth = 8;
    private const int MaxLineLength = 2000;

    public const string DenyTermRuleId = "deny-term";

    internal static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", ".vscode", "bin", "obj", "node_modules", "packages", "__pycache__", ".venv", "venv",
        "env", ".tox", ".mypy_cache", ".pytest_cache", "site-packages", "dist", "TestResults",
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".py", ".pyw", ".js", ".mjs", ".ts", ".ps1", ".psm1", ".bat", ".cmd", ".vbs", ".sh",
        ".json", ".config", ".xml", ".yaml", ".yml", ".ini", ".toml", ".csproj",
    };

    public static GuardReport Scan(string folder, LocalPolicy? policy = null, GuardRuleSet? rules = null)
    {
        rules ??= GuardRuleSet.Default;
        policy ??= LocalPolicy.Empty;
        var report = new GuardReport { ToolName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) };
        if (!Directory.Exists(folder)) return report;

        int files = 0;
        foreach (var file in EnumerateFiles(folder))
        {
            if (++files > MaxFiles) break;
            ScanFile(folder, file, policy, rules, report.Findings);
        }
        return report;
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var pending = new Stack<(string Dir, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            var (dir, depth) = pending.Pop();
            string[] subdirs, files;
            try
            {
                subdirs = Directory.GetDirectories(dir);
                files = Directory.GetFiles(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
                if (TextExtensions.Contains(Path.GetExtension(f))) yield return f;

            if (depth >= MaxDepth) continue;
            Array.Sort(subdirs, StringComparer.OrdinalIgnoreCase);
            for (int i = subdirs.Length - 1; i >= 0; i--)
                if (!Skipped.Contains(Path.GetFileName(subdirs[i]))) pending.Push((subdirs[i], depth + 1));
        }
    }

    private static void ScanFile(string root, string file, LocalPolicy policy, GuardRuleSet rules, List<GuardFinding> findings)
    {
        string[] lines;
        try
        {
            if (new FileInfo(file).Length > MaxFileBytes) return;
            lines = File.ReadAllLines(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }

        var relative = Path.GetRelativePath(root, file);
        var perRule = new Dictionary<string, int>();
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Length > MaxLineLength ? lines[i][..MaxLineLength] : lines[i];
            if (line.Length == 0) continue;

            foreach (var (rule, regex) in rules.Compiled)
            {
                var key = rule.Id;
                if (perRule.GetValueOrDefault(key) >= MaxFindingsPerRulePerFile) continue;
                bool hit;
                try { hit = regex.IsMatch(line); }
                catch (RegexMatchTimeoutException) { continue; }
                if (!hit) continue;
                perRule[key] = perRule.GetValueOrDefault(key) + 1;
                findings.Add(new GuardFinding(rule.Severity, rule.Id, relative, i + 1, rule.Message, rule.Why, rule.Fix));
            }

            if (policy.DenyTerms.Count > 0 && perRule.GetValueOrDefault(DenyTermRuleId) < MaxFindingsPerRulePerFile && policy.Contains(line))
            {
                perRule[DenyTermRuleId] = perRule.GetValueOrDefault(DenyTermRuleId) + 1;
                findings.Add(new GuardFinding(GuardSeverity.High, DenyTermRuleId, relative, i + 1,
                    "Contains a term from the local deny-list",
                    "The local policy lists this term as not to be used in tools or AI prompts.",
                    "Remove or replace it before the tool is approved."));
            }
        }
    }
}
