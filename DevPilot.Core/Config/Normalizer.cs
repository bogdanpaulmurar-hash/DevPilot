using DevPilot.Core.Hub;
using DevPilot.Models;

namespace DevPilot.Core.Config;

/// <summary>Repairs a loaded configuration so the rest of the app can trust it.</summary>
public static class Normalizer
{
    public static void Normalize(AppSettings s)
    {
        s.SchemaVersion = AppSettings.CurrentSchema;
        s.Groups ??= [];
        s.Tools ??= [];
        s.Hotkeys ??= [];
        s.Hub ??= new HubAppearance();
        s.Hub.Spotlight ??= new SpotlightOptions();
        s.KeepAwake ??= new KeepAwakeSettings();
        s.Startup ??= new StartupSettings();
        s.Startup.TakenOver ??= [];
        s.Monitor ??= new MonitorSettings();
        s.Monitor.ExtraTargets ??= [];
        s.Typer ??= new TyperSettings();
        s.Window ??= new WindowSettings();
        s.Backup ??= new BackupSettings();
        s.Backup.Keep = Math.Clamp(s.Backup.Keep, 1, BackupSettings.MaxKeep);
        s.Backup.Folder ??= string.Empty;
        s.ToolLog ??= new ToolLogSettings();
        s.ToolLog.MaxKb = Math.Clamp(s.ToolLog.MaxKb, 64, 4096);
        s.ToolLog.Keep = Math.Clamp(s.ToolLog.Keep, 1, 10);
        s.Accent = ConfigMigrator.NormalizeColor(s.Accent) is { Length: > 0 } accent ? accent : "#4F8EF7";

        // Unique ids
        var seen = new HashSet<string>();
        foreach (var g in s.Groups)
            if (string.IsNullOrWhiteSpace(g.Id) || !seen.Add(g.Id)) { g.Id = NewId.Short(); seen.Add(g.Id); }
        foreach (var g in s.Groups)
        {
            g.Color = ConfigMigrator.NormalizeColor(g.Color);
            g.TextColor = ConfigMigrator.NormalizeColor(g.TextColor);
            g.GlyphColor = ConfigMigrator.NormalizeColor(g.GlyphColor);
        }
        seen.Clear();
        foreach (var t in s.Tools)
        {
            if (string.IsNullOrWhiteSpace(t.Id) || !seen.Add(t.Id)) { t.Id = NewId.Short(); seen.Add(t.Id); }
            t.Actions ??= [];
            t.Managed ??= new ManagedOptions();
            t.Folder ??= new FolderOptions();
            t.AutoStart ??= new AutoStartOptions();
            t.Monitor ??= new MonitorOptions();
            var aseen = new HashSet<string>();
            foreach (var a in t.Actions)
                if (string.IsNullOrWhiteSpace(a.Id) || !aseen.Add(a.Id)) { a.Id = NewId.Short(); aseen.Add(a.Id); }
            t.Folder.Depth = Math.Clamp(t.Folder.Depth, 1, 5);
            t.Folder.MaxItems = Math.Clamp(t.Folder.MaxItems, 5, 500);
            t.AutoStart.CloudTimeoutSeconds = Math.Clamp(t.AutoStart.CloudTimeoutSeconds, 5, 3600);
            t.Color = ConfigMigrator.NormalizeColor(t.Color);
            t.TextColor = ConfigMigrator.NormalizeColor(t.TextColor);
            t.GlyphColor = ConfigMigrator.NormalizeColor(t.GlyphColor);
            if (t.IsSeparator)
            {
                // A separator never runs, tracks or owns a tray icon.
                t.Tray = TrayMode.HubOnly;
                t.AutoStart.Enabled = false;
                t.Spotlight = false;
                t.Managed.Enabled = false;
            }
            Presets.ToolPresets.UpgradeLegacyActions(t);
        }

        // Every tool belongs to an existing group.
        if (s.Tools.Count > 0 && s.Groups.Count == 0)
            s.Groups.Add(new ToolGroup { Title = "Tools" });
        var groupIds = s.Groups.Select(g => g.Id).ToHashSet();
        foreach (var t in s.Tools.Where(t => !groupIds.Contains(t.GroupId)))
            t.GroupId = s.Groups[0].Id;

        Ordering.Renumber(s);

        var h = s.Hub;
        h.Width = Math.Clamp(h.Width, 300, 900);
        h.MaxHeight = Math.Clamp(h.MaxHeight, 300, 1600);
        h.CornerRadius = Math.Clamp(h.CornerRadius, 0, 24);
        h.Opacity = Math.Clamp(h.Opacity, 0.7, 1.0);
        h.FontSize = Math.Clamp(h.FontSize, 10, 20);
        h.FlyoutWidth = Math.Clamp(h.FlyoutWidth, 180, 600);
        h.HoverDelayMs = Math.Clamp(h.HoverDelayMs, 0, 2000);
        h.HighlightStrength = Math.Clamp(h.HighlightStrength, RowColors.MinStrength, RowColors.MaxStrength);
        h.LabelMode = HubLabel.HubDefault(h.LabelMode);
        h.Spotlight.RecentCount = Math.Clamp(h.Spotlight.RecentCount, 0, 8);
        h.Spotlight.Accent = ConfigMigrator.NormalizeColor(h.Spotlight.Accent);

        var k = s.KeepAwake;
        k.IntervalSeconds = Math.Clamp(k.IntervalSeconds, 10, 600);
        k.OnlyWhenIdleSeconds = Math.Clamp(k.OnlyWhenIdleSeconds, 0, 3600);
        k.ScheduleDays ??= [1, 2, 3, 4, 5];
        s.Monitor.IntervalSeconds = Math.Clamp(s.Monitor.IntervalSeconds, 2, 300);
        s.Startup.RecheckMinutes = Math.Clamp(s.Startup.RecheckMinutes, 0, 240);

        int nextHk = s.Hotkeys.Count == 0 ? 1 : s.Hotkeys.Max(x => x.Id) + 1;
        var hkSeen = new HashSet<int>();
        foreach (var hk in s.Hotkeys)
            if (hk.Id <= 0 || !hkSeen.Add(hk.Id)) { hk.Id = nextHk++; hkSeen.Add(hk.Id); }
    }
}
