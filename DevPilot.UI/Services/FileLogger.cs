using System.Text;
using Microsoft.Extensions.Logging;

namespace DevPilot.UI.Services;

/// <summary>Small rolling file logger: %LOCALAPPDATA%\DevPilot\logs\devpilot.log (1 MB, 3 files).</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly object _gate = new();
    public string FilePath { get; }

    public FileLoggerProvider()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevPilot", "logs");
        Directory.CreateDirectory(dir);
        FilePath = Path.Combine(dir, "devpilot.log");
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal void Write(string line)
    {
        lock (_gate)
        {
            try
            {
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 1_000_000)
                {
                    var old2 = FilePath + ".2";
                    var old1 = FilePath + ".1";
                    if (File.Exists(old2)) File.Delete(old2);
                    if (File.Exists(old1)) File.Move(old1, old2);
                    File.Move(FilePath, old1);
                }
                File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public void Dispose() { }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        private readonly string _short = category[(category.LastIndexOf('.') + 1)..];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            var msg = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level.ToString()[..4].ToUpperInvariant()} {_short}: {formatter(state, ex)}";
            if (ex is not null) msg += Environment.NewLine + ex;
            owner.Write(msg);
        }
    }
}
