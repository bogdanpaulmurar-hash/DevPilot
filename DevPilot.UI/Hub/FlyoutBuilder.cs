using System.IO;
using DevPilot.Core.Folders;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Launch;
using DevPilot.Core.Presets;
using DevPilot.Models;
using DevPilot.UI.Services;

namespace DevPilot.UI.Hub;

/// <summary>
/// Builds the option lists shown when hovering a tool: its own actions (what used to be in its tray menu),
/// start / restart / stop, location, and for folders the folder content (nested, lazy).
/// </summary>
public sealed class FlyoutBuilder
{
    private readonly ILauncher _launcher;
    private readonly IProcessMonitor _monitor;
    private readonly IConfigStore _config;

    /// <summary>Set by the app: opens the main window on the tool editor.</summary>
    public Action<ToolEntry>? EditTool { get; set; }

    public FlyoutBuilder(ILauncher launcher, IProcessMonitor monitor, IConfigStore config)
    {
        _launcher = launcher;
        _monitor = monitor;
        _config = config;
    }

    public static string GlyphOf(ToolEntry t) => string.IsNullOrWhiteSpace(t.Glyph) ? Glyphs.ForKind(t.Kind) : t.Glyph;

    public bool HasFlyout(ToolEntry t) =>
        t.Actions.Count > 0 || t.IsProcessLike || (t.IsFolderLike && t.Folder.ExpandInHub) || t.Kind is ToolKind.Url or ToolKind.Document;

    public IReadOnlyList<FlyoutItem> ForTool(ToolEntry tool, bool includeFolderContent = true)
    {
        var items = new List<FlyoutItem>();
        var status = _monitor.Get(tool.Id);

        if (tool.IsFolderLike && tool.Folder.ExpandInHub && includeFolderContent)
        {
            var path = PathTokens.Expand(tool.Path, tool);
            items.AddRange(FolderItems(path, tool.Folder, 1));
            items.Add(FlyoutItem.Separator());
            items.Add(FlyoutItem.Action("Open folder", Glyphs.FolderOpen, () => Run(_launcher.StartAsync(tool), tool.Name), bold: true));
        }

        // Tool actions, grouped by section
        string? section = null;
        foreach (var a in tool.Actions)
        {
            if (a.Kind == ActionKind.Separator) { items.Add(FlyoutItem.Separator()); section = null; continue; }
            if (!string.IsNullOrWhiteSpace(a.Section) && a.Section != section)
            {
                section = a.Section;
                items.Add(FlyoutItem.Header(section));
            }
            var action = a;
            items.Add(new FlyoutItem
            {
                Label = action.Label,
                Glyph = string.IsNullOrWhiteSpace(action.Glyph) ? Glyphs.ForAction(action.Kind) : action.Glyph,
                IsBold = action.Bold,
                Invoke = () => Run(_launcher.RunActionAsync(tool, action), $"{tool.Name} — {action.Label}"),
            });
        }

        if (tool.IsProcessLike)
        {
            if (items.Count > 0) items.Add(FlyoutItem.Separator());
            bool running = status.State is ToolRunState.Running or ToolRunState.Starting;
            if (!running)
                items.Add(FlyoutItem.Action("Start", Glyphs.Play, () => Run(_launcher.StartAsync(tool), tool.Name), bold: tool.Actions.Count == 0));
            else
            {
                items.Add(FlyoutItem.Action("Open / start again", Glyphs.Play, () => Run(_launcher.StartAsync(tool), tool.Name)));
                if (ProcessMatcher.CanTrack(tool))
                {
                    items.Add(FlyoutItem.Action("Restart", Glyphs.Refresh, () => Run(_launcher.RestartAsync(tool), tool.Name)));
                    items.Add(new FlyoutItem
                    {
                        Label = "Stop", Glyph = Glyphs.Stop, IsDanger = true, Detail = status.Detail,
                        Invoke = async () => { await _launcher.StopAsync(tool); return true; },
                    });
                }
            }
        }
        else if (!tool.IsFolderLike || !tool.Folder.ExpandInHub)
        {
            if (items.Count > 0) items.Add(FlyoutItem.Separator());
            items.Add(FlyoutItem.Action(tool.Kind == ToolKind.Url ? "Open link" : "Open", GlyphOf(tool), () => Run(_launcher.StartAsync(tool), tool.Name), bold: true));
        }

        if (tool.Kind is not ToolKind.Url and not ToolKind.Ssh and not ToolKind.Wsl and not ToolKind.ShellCommand && !string.IsNullOrWhiteSpace(tool.Path + tool.WorkingDirectory))
            items.Add(FlyoutItem.Action("Open location", Glyphs.FolderOpen, () => Run(_launcher.OpenLocationAsync(tool), tool.Name)));
        if (EditTool is not null)
            items.Add(FlyoutItem.Action("Edit in DevPilot", Glyphs.Edit, () => EditTool(tool)));
        return items;
    }

    public IReadOnlyList<FlyoutItem> FolderItems(string path, FolderOptions options, int level)
    {
        var listing = FolderBrowser.List(path, options);
        if (listing.Error is not null) return [FlyoutItem.Info(listing.Error, Glyphs.Warning)];
        if (listing.Items.Count == 0) return [FlyoutItem.Info("Empty folder")];
        var list = new List<FlyoutItem>(listing.Items.Count + 2);
        foreach (var item in listing.Items)
        {
            var it = item;
            if (it.IsDirectory)
            {
                list.Add(new FlyoutItem
                {
                    Label = it.Name,
                    Glyph = Glyphs.Folder,
                    Invoke = () => Run(_launcher.OpenPathAsync(it.FullPath), it.Name),
                    Children = level < options.Depth ? () => FolderChildren(it.FullPath, options, level + 1) : null,
                    ToolTip = it.FullPath,
                });
            }
            else
            {
                list.Add(new FlyoutItem
                {
                    Label = it.Name,
                    Glyph = FileGlyph(it.Name),
                    Detail = FormatSize(it.Size),
                    Invoke = () => Run(_launcher.OpenPathAsync(it.FullPath), it.Name),
                    ToolTip = $"{it.FullPath}\n{it.Modified:g}",
                });
            }
        }
        if (listing.Truncated > 0) list.Add(FlyoutItem.Info($"+ {listing.Truncated} more"));
        return list;
    }

    private IReadOnlyList<FlyoutItem> FolderChildren(string path, FolderOptions options, int level)
    {
        var items = new List<FlyoutItem>(FolderItems(path, options, level))
        {
            FlyoutItem.Separator(),
            FlyoutItem.Action("Open this folder", Glyphs.FolderOpen, () => Run(_launcher.OpenPathAsync(path), Path.GetFileName(path))),
        };
        return items;
    }

    public static string FileGlyph(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" => Glyphs.Picture,
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" => Glyphs.Video,
        ".exe" or ".msi" => Glyphs.App,
        ".py" or ".pyw" or ".ps1" or ".bat" or ".cmd" or ".cs" or ".js" or ".json" or ".xml" => Glyphs.Code,
        ".url" or ".lnk" => Glyphs.Link,
        _ => Glyphs.Document,
    };

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.##} GB",
    };

    /// <summary>Awaits a launch and reports failures as a notification. Returns true to close the hub.</summary>
    public static async Task<bool> Run(Task<LaunchResult> launch, string what)
    {
        var r = await launch;
        if (!r.Success) Notifier.Error($"Could not start {what}", r.Error ?? "Unknown error");
        return r.Success;
    }
}
