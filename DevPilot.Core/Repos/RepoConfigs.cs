using DevPilot.Models;

namespace DevPilot.Core.Repos;

/// <summary>One thing the user can run on a repository: the git pull, or one of its commands.</summary>
public sealed record RepoAction(string Name, RepoCommand? Command)
{
    public bool IsPull => Command is null;
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

    /// <summary>The actions of a repository in the order shown: git pull first (unless switched off), then its commands.</summary>
    public static IReadOnlyList<RepoAction> Actions(RepoSettings s, string repo)
    {
        var cfg = Find(s, repo);
        var list = new List<RepoAction>();
        if (cfg is null || cfg.IncludePull) list.Add(new RepoAction("git pull", null));
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
            bool useless = item.IncludePull && item.Commands.Count == 0;
            // The first entry for a folder wins.
            if (item.Path.Length > 0 && !useless && seenPaths.Add(item.Path)) kept.Add(item);
        }
        s.Items = kept;
    }
}
