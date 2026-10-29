using System.Text.Json;
using DevPilot.Core.Config;

namespace DevPilot.Core.Guard;

/// <summary>
/// Optional machine-local rules from <c>%APPDATA%\DevPilot\policy.local.json</c>. The file is never part of the
/// repository, of backups or of reports. Absent or unreadable = empty policy. Callers match through
/// <see cref="Contains"/>; <see cref="ToString"/> and <see cref="LoadProblem"/> never include the terms.
/// </summary>
public sealed class LocalPolicy
{
    public const string FileName = "policy.local.json";

    public static LocalPolicy Empty { get; } = new();

    public List<string> DenyTerms { get; set; } = [];

    /// <summary>Hosts every tool may reach in addition to its own allowlist (for example a company proxy).</summary>
    public List<string> AlwaysAllowedHosts { get; set; } = [];

    /// <summary>Why the file was ignored, if it was; contains no file content.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string? LoadProblem { get; private set; }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DevPilot", FileName);

    public static LocalPolicy Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return new LocalPolicy();
        try
        {
            var policy = JsonSerializer.Deserialize<LocalPolicy>(File.ReadAllText(path), ConfigSerializer.Options)
                         ?? new LocalPolicy();
            policy.DenyTerms = policy.DenyTerms.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
            return policy;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new LocalPolicy { LoadProblem = ex.GetType().Name };
        }
    }

    /// <summary>True when <paramref name="text"/> contains any deny-list term (case-insensitive).</summary>
    public bool Contains(string text) =>
        DenyTerms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));

    public bool IsAlwaysAllowed(string host) =>
        AlwaysAllowedHosts.Any(p => ToolNetworkPolicy.HostMatches(p, host));

    public override string ToString() => $"LocalPolicy({DenyTerms.Count} deny terms, {AlwaysAllowedHosts.Count} hosts)";
}
