using DevPilot.Models;

namespace DevPilot.Core.Repos;

public enum RepoActionKind { Pull, Update, Command }

/// <summary>One thing the user can run on a repository: update, a plain git pull, or one of its commands.</summary>
public sealed record RepoAction(string Name, RepoCommand? Command, RepoActionKind Kind = RepoActionKind.Command)
{
    public bool IsPull => Kind == RepoActionKind.Pull;
    public bool IsUpdate => Kind == RepoActionKind.Update;

    /// <summary>Only one copy of a repository that has a cloud and a local copy; null = every copy.</summary>
    public RepoCopyKind? Only { get; init; }

    public static RepoAction Pull() => new("git pull", null, RepoActionKind.Pull);
    public static RepoAction Pull(RepoCopyKind only) =>
        new(only == RepoCopyKind.Cloud ? "Pull the cloud copy" : "Pull the local copy", null, RepoActionKind.Pull) { Only = only };
    public static RepoAction Update() => new("Update", null, RepoActionKind.Update);
}

/// <summary>Lookups and repairs for the per-repository settings.</summary>
public static class RepoConfigs
{
    public static RepoConfig? Find(RepoSettings s, string repo) =>
        s.Items.FirstOrDefault(i => RepoScanner.SamePath(i.Path, repo));

    public static RepoConfig GetOrCreate(RepoSettings s, string repo)
    {
        var found = Find(s, repo);
        if (found is not null) return found;
        var created = new RepoConfig { Path = RepoScanner.Normalize(repo) };
        s.Items.Add(created);
        return created;
    }

    /// <summary>
    /// The actions of a repository in the order shown: Update (when <paramref name="plan"/> says there is one), git pull
    /// (always, never, or only when there is no Update), then its commands.
    /// </summary>
    public static IReadOnlyList<RepoAction> Actions(RepoSettings s, string repo, RepoUpdatePlan? plan = null)
    {
        var cfg = Find(s, repo);
        var list = new List<RepoAction>();
        bool update = plan is { Available: true };
        if (update) list.Add(RepoAction.Update());
        var pull = cfg?.Pull ?? PullButton.Auto;
        if (pull == PullButton.Always || (pull == PullButton.Auto && !update)) list.Add(RepoAction.Pull());
        if (cfg is not null)
            list.AddRange(cfg.Commands.Where(c => !string.IsNullOrWhiteSpace(c.CommandLine)).Select(c => new RepoAction(DisplayName(c), c)));
        return list;
    }

    /// <summary>The name, or the first word of the command line when the name is empty.</summary>
    public static string DisplayName(RepoCommand c)
    {
        if (!string.IsNullOrWhiteSpace(c.Name)) return c.Name.Trim();
        var first = c.CommandLine.Trim().Split(' ', '\t')[0].Trim('"');
        return first.Length > 0 ? Path.GetFileName(first) is { Length: > 0 } f ? f : first : "Command";
    }

    /// <summary>Drops commands without a command line and entries without a path or any customisation; keeps ids unique.</summary>
    public static void Normalize(RepoSettings s)
    {
        s.Items ??= [];
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<RepoConfig>();
        foreach (var item in s.Items)
        {
            item.Commands ??= [];
            item.Path = RepoScanner.Normalize(item.Path ?? string.Empty);
            item.Commands.RemoveAll(c => string.IsNullOrWhiteSpace(c.CommandLine));
            var ids = new HashSet<string>();
            foreach (var c in item.Commands)
            {
                c.Name = c.Name?.Trim() ?? string.Empty;
                c.CommandLine = c.CommandLine.Trim();
                if (!Enum.IsDefined(c.Mode)) c.Mode = RepoCommandMode.Hidden;
                if (string.IsNullOrWhiteSpace(c.Id) || !ids.Add(c.Id)) { c.Id = NewId.Short(); ids.Add(c.Id); }
            }
            item.ToolIds = (item.ToolIds ?? []).Where(i => !string.IsNullOrWhiteSpace(i)).Distinct().ToList();
            item.InstallCommand = item.InstallCommand?.Trim() ?? string.Empty;
            item.CloudPath = CleanPath(item.CloudPath);
            item.LocalPath = CleanPath(item.LocalPath);
            if (!Enum.IsDefined(item.Pull)) item.Pull = PullButton.Auto;
            bool useless = item.Pull == PullButton.Auto && item.UpdateEnabled && item.RestartTools
                           && item.InstallCommand.Length == 0 && item.Commands.Count == 0 && item.ToolIds.Count == 0
                           && item.CloudPath.Length == 0 && item.LocalPath.Length == 0;
            // The first entry for a folder wins.
            if (item.Path.Length > 0 && !useless && seenPaths.Add(item.Path)) kept.Add(item);
        }
        s.Items = kept;
    }

    /// <summary>A typed or pasted folder: trimmed, quotes removed, normalised; empty stays empty.</summary>
    public static string CleanPath(string? path)
    {
        var p = (path ?? string.Empty).Trim().Trim('"').Trim();
        return p.Length == 0 ? string.Empty : RepoScanner.Normalize(p);
    }
}
