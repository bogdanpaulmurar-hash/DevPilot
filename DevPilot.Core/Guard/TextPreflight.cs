using System.Text;
using System.Text.RegularExpressions;

namespace DevPilot.Core.Guard;

/// <summary>Where something suspicious sits in a text. Never carries the text itself.</summary>
public sealed record PreflightHit(string Category, int Line, int Column);

public sealed class PreflightResult
{
    public List<PreflightHit> Hits { get; } = [];
    public bool Truncated { get; set; }
    public bool IsClean => Hits.Count == 0;

    /// <summary>Counts per category, most frequent first.</summary>
    public IEnumerable<(string Category, int Count)> ByCategory() =>
        Hits.GroupBy(h => h.Category).Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2).ThenBy(x => x.Key);

    /// <summary>A short report with categories and positions only (what is shown to the user).</summary>
    public string Describe(int maxLines = 20)
    {
        if (IsClean) return "Nothing suspicious found.";
        var sb = new StringBuilder();
        sb.AppendLine($"{Hits.Count}{(Truncated ? "+" : "")} possible problem(s):");
        foreach (var (category, count) in ByCategory()) sb.AppendLine($"  {category}: {count}");
        sb.AppendLine();
        foreach (var h in Hits.Take(maxLines)) sb.AppendLine($"  line {h.Line}, column {h.Column}: {h.Category}");
        if (Hits.Count > maxLines) sb.AppendLine($"  … and {Hits.Count - maxLines} more");
        sb.AppendLine();
        sb.Append("The matched text is not shown. Remove or replace it before pasting the text into an AI tool.");
        return sb.ToString();
    }
}

/// <summary>
/// Checks a text (clipboard or file) for the local deny-list terms and common secret / personal-data patterns
/// before it is pasted into an AI tool. Reports only the category and position, never the matched text.
/// This is a safety net, not a guarantee: it cannot recognise everything.
/// </summary>
public static class TextPreflight
{
    public const int MaxHits = 200;
    public const string DenyCategory = "Term from the local deny-list";
    public const string SecretCategory = "Possible secret or key";
    public const string EmailCategory = "E-mail address";
    public const string IbanCategory = "IBAN";
    public const string CardCategory = "Payment card number";
    public const string PhoneCategory = "Phone number";

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);
    private const int MaxLineLength = 5000;

    private static readonly Regex Email = new(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Iban = new(@"\b[A-Z]{2}\d{2}[A-Z0-9]{11,30}\b", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Card = new(@"\b(?:\d[ -]?){12,18}\d\b", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Phone = new(@"(?<![\w+])\+\d[\d ()-]{7,16}\d\b", RegexOptions.CultureInvariant, Timeout);

    public static PreflightResult Check(string? text, LocalPolicy? policy = null)
    {
        var result = new PreflightResult();
        if (string.IsNullOrEmpty(text)) return result;
        policy ??= LocalPolicy.Empty;
        var secret = GuardRuleSet.Default.Compiled.FirstOrDefault(r => r.Rule.Id == "secret").Regex;

        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length && !result.Truncated; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length > MaxLineLength) line = line[..MaxLineLength];
            if (line.Length == 0) continue;

            foreach (var at in policy.FindDenyMatches(line))
                Add(result, DenyCategory, i, at);

            if (secret is not null) AddMatches(result, secret, SecretCategory, line, i, _ => true);
            AddMatches(result, Email, EmailCategory, line, i, _ => true);
            AddMatches(result, Iban, IbanCategory, line, i, IsValidIban);
            AddMatches(result, Card, CardCategory, line, i, IsValidCard);
            AddMatches(result, Phone, PhoneCategory, line, i, _ => true);
        }
        result.Hits.Sort((a, b) => a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.Column.CompareTo(b.Column));
        return result;
    }

    private static void AddMatches(PreflightResult result, Regex regex, string category, string line, int lineIndex, Func<string, bool> accept)
    {
        try
        {
            foreach (Match m in regex.Matches(line))
                if (accept(m.Value)) Add(result, category, lineIndex, m.Index);
        }
        catch (RegexMatchTimeoutException) { }
    }

    private static void Add(PreflightResult result, string category, int lineIndex, int column)
    {
        if (result.Hits.Count >= MaxHits) { result.Truncated = true; return; }
        result.Hits.Add(new PreflightHit(category, lineIndex + 1, column + 1));
    }

    /// <summary>Luhn check on 13 to 19 digits.</summary>
    internal static bool IsValidCard(string text)
    {
        var digits = text.Where(char.IsDigit).Select(c => c - '0').ToArray();
        if (digits.Length is < 13 or > 19 || digits.Distinct().Count() == 1) return false;
        int sum = 0;
        for (int i = 0; i < digits.Length; i++)
        {
            int d = digits[digits.Length - 1 - i];
            if (i % 2 == 1) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
        }
        return sum % 10 == 0;
    }

    /// <summary>ISO 13616 check: move the first four characters to the end, letters become 10..35, remainder mod 97 is 1.</summary>
    internal static bool IsValidIban(string text)
    {
        if (text.Length is < 15 or > 34) return false;
        var rearranged = text[4..] + text[..4];
        int remainder = 0;
        foreach (var c in rearranged)
        {
            if (char.IsDigit(c)) remainder = (remainder * 10 + (c - '0')) % 97;
            else if (c is >= 'A' and <= 'Z') remainder = (remainder * 100 + (c - 'A' + 10)) % 97;
            else return false;
        }
        return remainder == 1;
    }
}
