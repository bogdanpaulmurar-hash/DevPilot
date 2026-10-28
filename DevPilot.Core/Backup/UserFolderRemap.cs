using System.Text;
using DevPilot.Models;

namespace DevPilot.Core.Backup;

/// <summary>
/// Rewrites paths under one Windows user folder to another (C:\Users\old\Dev\x → C:\Users\new\Dev\x),
/// so a backup restored on a PC with a different user name still finds the tools.
/// </summary>
public static class UserFolderRemap
{
    /// <summary>Replaces every occurrence of <paramref name="oldRoot"/> that is a whole path prefix (case-insensitive).</summary>
    public static string Replace(string? value, string oldRoot, string newRoot)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        oldRoot = (oldRoot ?? string.Empty).Trim().TrimEnd('\\', '/');
        newRoot = (newRoot ?? string.Empty).Trim().TrimEnd('\\', '/');
        if (oldRoot.Length < 3 || newRoot.Length == 0 || oldRoot.Equals(newRoot, StringComparison.OrdinalIgnoreCase)) return value;

        var sb = new StringBuilder(value.Length);
        int i = 0;
        while (i < value.Length)
        {
            int idx = value.IndexOf(oldRoot, i, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) { sb.Append(value, i, value.Length - i); break; }
            int end = idx + oldRoot.Length;
            bool startsPath = idx == 0 || !(char.IsLetterOrDigit(value[idx - 1]) || value[idx - 1] is '\\' or '/' or '_' or '.');
            bool endsSegment = end == value.Length || value[end] is '\\' or '/' or '"' or '\'' or ';';
            sb.Append(value, i, idx - i);
            sb.Append(startsPath && endsSegment ? newRoot : value.Substring(idx, oldRoot.Length));
            i = end;
        }
        return sb.ToString();
    }

    /// <summary>Applies <see cref="Replace"/> to every path-like field of the tools; returns how many fields changed.</summary>
    public static int Apply(AppSettings settings, string oldRoot, string newRoot)
    {
        int changed = 0;
        string Map(string v)
        {
            var n = Replace(v, oldRoot, newRoot);
            if (!string.Equals(n, v, StringComparison.Ordinal)) changed++;
            return n;
        }
        foreach (var t in settings.Tools)
        {
            t.Path = Map(t.Path);
            t.Arguments = Map(t.Arguments);
            t.WorkingDirectory = Map(t.WorkingDirectory);
            t.Interpreter = Map(t.Interpreter);
            t.IconPath = Map(t.IconPath);
            foreach (var a in t.Actions ?? [])
            {
                a.Value = Map(a.Value);
                a.Arguments = Map(a.Arguments);
            }
        }
        foreach (var g in settings.Groups ?? [])
            g.IconPath = Map(g.IconPath);
        return changed;
    }
}
