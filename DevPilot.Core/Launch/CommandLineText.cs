using System.Text;

namespace DevPilot.Core.Launch;

public static class CommandLineText
{
    /// <summary>Quotes one argument the way CommandLineToArgvW expects.</summary>
    public static string Quote(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return "\"\"";
        if (arg.IndexOfAny([' ', '\t', '"']) < 0) return arg;
        if (arg.Length >= 2 && arg[0] == '"' && arg[^1] == '"') return arg;
        var sb = new StringBuilder("\"");
        int backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\') { backslashes++; continue; }
            if (c == '"') { sb.Append('\\', backslashes * 2 + 1); sb.Append('"'); }
            else { sb.Append('\\', backslashes); sb.Append(c); }
            backslashes = 0;
        }
        sb.Append('\\', backslashes * 2);
        sb.Append('"');
        return sb.ToString();
    }

    public static string Join(params string?[] parts) =>
        string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
}
