using DevPilot.Core.Interfaces;
using DevPilot.Core.Logging;
using DevPilot.Models;
using Microsoft.Extensions.Logging;

namespace DevPilot.Infrastructure.Services;

/// <summary>Writes %LOCALAPPDATA%\DevPilot\logs\tools\name-id.log (size from settings, rotated).</summary>
public sealed class ToolLogService : IToolLog
{
    private readonly ILogger<ToolLogService> _logger;
    private readonly IConfigStore _config;
    private readonly object _gate = new();

    public string Folder { get; }

    public ToolLogService(ILogger<ToolLogService> logger, IConfigStore config, string? folder = null)
    {
        _logger = logger;
        _config = config;
        Folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevPilot", "logs", "tools");
    }

    public string FilePath(ToolEntry tool) => Path.Combine(Folder, ToolLogFiles.FileName(tool));

    public void Write(ToolEntry tool, string text)
    {
        var s = _config.Current.ToolLog;
        if (s is null || !s.Enabled || tool.IsSeparator) return;
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var path = FilePath(tool);
                ToolLogFiles.RotateIfNeeded(path, Math.Clamp(s.MaxKb, 64, 4096) * 1024L, Math.Clamp(s.Keep, 1, 10));
                File.AppendAllText(path, ToolLogFormat.Stamp(DateTime.Now, text) + Environment.NewLine, System.Text.Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Tool log write failed for {Tool}", tool.Name);
            }
        }
    }

    public string ReadTail(ToolEntry tool, int maxLines = 200)
    {
        try { lock (_gate) return ToolLogFiles.Tail(FilePath(tool), maxLines); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return string.Empty; }
    }

    public void Prune(IEnumerable<ToolEntry> tools)
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            lock (_gate)
                foreach (var f in ToolLogFiles.Orphans(Directory.EnumerateFiles(Folder), tools))
                    File.Delete(f);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Tool log cleanup failed");
        }
    }
}
