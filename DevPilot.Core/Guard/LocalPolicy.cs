using System.Text.Json;
using System.Text.RegularExpressions;
using DevPilot.Core.Config;

namespace DevPilot.Core.Guard;

/// <summary>
/// Optional machine-local rules from <c>%APPDATA%\DevPilot\policy.local.json</c>. The file is never part of the
/// repository, of backups or of reports. Absent or unreadable = empty policy. Callers match through
/// <see cref="Contains"/>; <see cref="ToString"/> and <see cref="LoadProblem"/> never include the terms.
/// </summary>
public sealed class LocalPolicy
{
    public const string FileName = "policy.local.json";

    public static LocalPolicy Empty { get; } = new();

    public List<string> DenyTerms { get; set; } = [];

    /// <summary>Hosts every tool may reach in addition to its own allowlist (for example a company proxy).</summary>
    public List<string> AlwaysAllowedHosts { get; set; } = [];

    /// <summary>Why the file was ignored, if it was; contains no file content.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string? LoadProblem { get; private set; }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DevPilot", FileName);

    public static LocalPolicy Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return new LocalPolicy();
        try
        {
            var policy = JsonSerializer.Deserialize<LocalPolicy>(File.ReadAllText(path), ConfigSerializer.Options)
                         ?? new LocalPolicy();
            policy.DenyTerms = policy.DenyTerms.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
            return policy;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new LocalPolicy { LoadProblem = ex.GetType().Name };
        }
    }

    private const int WildcardSpan = 40;
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(200);
    private string _compiledFor = string.Empty;
    private List<Regex> _compiled = [];

    /// <summary>How many deny-list entries could not be used (for example a broken <c>re:</c> pattern); never their text.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public int InvalidTermCount { get; private set; }

    /// <summary>True when <paramref name="text"/> contains any deny-list term (case-insensitive).</summary>
    public bool Contains(string text) => FindDenyMatches(text).Any();

    /// <summary>
    /// Zero-based columns of every deny-list match in <paramref name="text"/>. A term is plain text, or uses
    /// <c>*</c> for "up to 40 characters" (<c>proiect*alfa</c>), or starts with <c>re:</c> for a regular expression.
    /// Leading and trailing <c>*</c> are redundant (matching is by substring) and are ignored.
    /// </summary>
    public IEnumerable<int> FindDenyMatches(string text)
    {
        foreach (var regex in Compiled())
        {
            MatchCollection matches;
            try { matches = regex.Matches(text); }
            catch (RegexMatchTimeoutException) { continue; }
            foreach (Match m in matches)
                if (m.Length > 0) yield return m.Index;
        }
    }

    private List<Regex> Compiled()
    {
        var key = string.Join('\n', DenyTerms);
        if (key == _compiledFor) return _compiled;
        var list = new List<Regex>();
        var invalid = 0;
        foreach (var term in DenyTerms)
        {
            var pattern = term.StartsWith("re:", StringComparison.OrdinalIgnoreCase)
                ? term[3..]
                : string.Join(".{0," + WildcardSpan + "}?", term.Trim('*').Split('*').Select(Regex.Escape));
            if (pattern.Length == 0 || term.Trim('*').Length == 0) { invalid++; continue; }
            try { list.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout)); }
            catch (ArgumentException) { invalid++; }
        }
        InvalidTermCount = invalid;
        _compiled = list;
        _compiledFor = key;
        return list;
    }

    public bool IsAlwaysAllowed(string host) =>
        AlwaysAllowedHosts.Any(p => ToolNetworkPolicy.HostMatches(p, host));

    public override string ToString() => $"LocalPolicy({DenyTerms.Count} deny terms, {AlwaysAllowedHosts.Count} hosts)";
}
