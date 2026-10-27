using DevPilot.Models;

namespace DevPilot.Core.Interfaces;

/// <summary>One log file per tool: what DevPilot started, with which arguments, how it ended.</summary>
public interface IToolLog
{
    string Folder { get; }
    string FilePath(ToolEntry tool);
    /// <summary>Appends a timestamped entry (multi-line text allowed). Never throws; does nothing when logging is off.</summary>
    void Write(ToolEntry tool, string text);
    string ReadTail(ToolEntry tool, int maxLines = 200);
    /// <summary>Deletes logs of tools that no longer exist.</summary>
    void Prune(IEnumerable<ToolEntry> tools);
}
