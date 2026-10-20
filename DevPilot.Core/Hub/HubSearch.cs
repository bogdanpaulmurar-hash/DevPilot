using System.Globalization;
using System.Text;
using DevPilot.Models;

namespace DevPilot.Core.Hub;

/// <summary>Instant search in the hub: name, description, group title, action labels; diacritics-insensitive.</summary>
public static class HubSearch
{
    public static bool Matches(ToolEntry tool, string? groupTitle, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var hay = Fold($"{tool.Name} {tool.Description} {groupTitle} {string.Join(' ', tool.Actions.Select(a => a.Label))} {tool.Preset}");
        return Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).All(hay.Contains);
    }

    public static bool ActionMatches(ToolAction action, string? query) =>
        string.IsNullOrWhiteSpace(query) || Fold($"{action.Label} {action.Section}").Contains(Fold(query).Trim());

    public static string Fold(string s)
    {
        var norm = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(norm.Length);
        foreach (var c in norm)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}
