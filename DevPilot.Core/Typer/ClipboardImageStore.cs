namespace DevPilot.Core.Typer;

/// <summary>
/// Keeps clipboard images as PNG files so their path can be typed into a chat that cannot paste images.
/// Old files are removed automatically (by age and by count), so the folder never grows.
/// </summary>
public sealed class ClipboardImageStore
{
    private const string Prefix = "clip-";
    private const string Extension = ".png";

    private readonly int _maxFiles;
    private readonly TimeSpan _maxAge;

    public string Folder { get; }

    public ClipboardImageStore(string folder, int maxFiles = 20, TimeSpan? maxAge = null)
    {
        Folder = folder;
        _maxFiles = Math.Max(1, maxFiles);
        _maxAge = maxAge ?? TimeSpan.FromHours(24);
    }

    /// <summary>Writes the PNG and returns its full path.</summary>
    public string Save(byte[] png)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, $"{Prefix}{DateTime.Now:yyyyMMdd-HHmmss-fff}{Extension}");
        File.WriteAllBytes(path, png);
        Cleanup();
        return path;
    }

    /// <summary>Deletes files older than the maximum age, then the oldest ones beyond the maximum count.</summary>
    public void Cleanup()
    {
        if (!Directory.Exists(Folder)) return;
        var files = new DirectoryInfo(Folder).GetFiles($"{Prefix}*{Extension}")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();
        var cutoff = DateTime.UtcNow - _maxAge;
        for (var i = 0; i < files.Count; i++)
        {
            if (i < _maxFiles && files[i].LastWriteTimeUtc >= cutoff) continue;
            try { files[i].Delete(); }
            catch (IOException) { /* in use by another program; the next cleanup retries */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
