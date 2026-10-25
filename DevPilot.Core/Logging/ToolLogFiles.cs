using System.Text;
using DevPilot.Models;

namespace DevPilot.Core.Logging;

/// <summary>File naming, size-based rotation and tail reading for the per-tool logs.</summary>
public static class ToolLogFiles
{
    public const string Extension = ".log";

    /// <summary><c>name-id.log</c>, with characters Windows does not allow in file names replaced by '_'.</summary>
    public static string FileName(ToolEntry tool)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string((tool.Name ?? string.Empty).Trim().Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray());
        if (name.Length > 40) name = name[..40];
        name = name.Trim('.', ' ');
        if (name.Length == 0) name = "tool";
        return $"{name}-{tool.Id}{Extension}";
    }

    /// <summary>When <paramref name="path"/> is at least <paramref name="maxBytes"/>: path.1 (newest old) … path.keep-1 (oldest); the rest is deleted.</summary>
    public static void RotateIfNeeded(string path, long maxBytes, int keep)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists || fi.Length < maxBytes) return;
        keep = Math.Max(1, keep);
        if (keep == 1) { fi.Delete(); return; }
        var oldest = $"{path}.{keep - 1}";
        if (File.Exists(oldest)) File.Delete(oldest);
        for (int i = keep - 2; i >= 1; i--)
        {
            var from = $"{path}.{i}";
            if (File.Exists(from)) File.Move(from, $"{path}.{i + 1}");
        }
        File.Move(path, path + ".1");
    }

    /// <summary>The last <paramref name="maxLines"/> lines; reads only the end of big files.</summary>
    public static string Tail(string path, int maxLines, int maxBytes = 256 * 1024)
    {
        if (!File.Exists(path)) return string.Empty;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var start = Math.Max(0, fs.Length - maxBytes);
        fs.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        var text = reader.ReadToEnd();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var skipFirst = start > 0 ? 1 : 0; // first line may be cut in the middle
        var all = lines.Skip(skipFirst).ToList();
        if (all.Count > 0 && all[^1].Length == 0) all.RemoveAt(all.Count - 1);
        return string.Join(Environment.NewLine, all.Skip(Math.Max(0, all.Count - maxLines)));
    }

    /// <summary>Log files (including rotated ones) whose tool id is not in <paramref name="validIds"/>.</summary>
    public static IReadOnlyList<string> Orphans(IEnumerable<string> files, IEnumerable<ToolEntry> tools)
    {
        var suffixes = tools.Select(t => $"-{t.Id}{Extension}").ToList();
        return files.Where(f =>
        {
            var n = Path.GetFileName(f);
            var baseName = n.Length > 2 && n[^2] == '.' && char.IsDigit(n[^1]) ? n[..^2] : n;
            return baseName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
                   && !suffixes.Any(s => baseName.EndsWith(s, StringComparison.OrdinalIgnoreCase));
        }).ToList();
    }
}
