using DevPilot.Core.Repos;
using DevPilot.Infrastructure.Services;

namespace DevPilot.UI.Services;

/// <summary>"Pull repositories" from the hub, the tray menu and the Repositories page: one place for the messages.</summary>
public static class RepoActions
{
    public static async Task PullNowAsync(RepoSyncService repos, IAppShell shell)
    {
        if (string.IsNullOrWhiteSpace(repos.Settings.Root))
        {
            shell.ShowMainWindow("repos");
            Notifier.Info("Pull repositories", "Choose the folder that holds your clones first.");
            return;
        }
        if (repos.IsRunning)
        {
            Notifier.Info("Pull repositories", "A pull is already running.");
            return;
        }
        Notifier.Info("Pulling repositories…", repos.Settings.Root);
        var results = await repos.PullSelectedAsync();
        if (results is null) return;
        Report(results, repos.Settings.Root);
    }

    /// <summary>Summary notification; names the repositories that need attention (at most three).</summary>
    public static void Report(IReadOnlyList<RepoPullResult> results, string root)
    {
        var summary = RepoPull.Summary(results.ToList());
        var problems = results.Where(r => !r.IsOk).ToList();
        if (problems.Count == 0)
        {
            Notifier.Info("Repositories pulled", summary);
            return;
        }
        var names = string.Join(", ", problems.Take(3).Select(r => $"{RepoScanner.DisplayName(root, r.Path)} ({r.StatusText.ToLowerInvariant()})"));
        if (problems.Count > 3) names += $" +{problems.Count - 3}";
        var text = summary + Environment.NewLine + names;
        if (problems.Any(r => !r.IsSkipped)) Notifier.Error("Repositories pulled with problems", text);
        else Notifier.Info("Repositories pulled", text);
    }
}
