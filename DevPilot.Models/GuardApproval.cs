namespace DevPilot.Models;

/// <summary>
/// What a tool looked like when the user approved it in the Egress Guard. Lives in the DevPilot config
/// (not in the tool folder). If the tool's files or git remote change afterwards, the approval no longer matches.
/// </summary>
public class GuardApproval
{
    /// <summary>SHA-256 over the sorted list of file paths and file hashes at approval time.</summary>
    public string ManifestDigest { get; set; } = string.Empty;
    public int FileCount { get; set; }
    /// <summary>Git remote URL without credentials; empty when the folder is not a git clone.</summary>
    public string GitRemote { get; set; } = string.Empty;
    public string GitCommit { get; set; } = string.Empty;
    public DateTime ApprovedUtc { get; set; }
    /// <summary>The guard mode (off / monitor / block) that was active when approving.</summary>
    public string ApprovedMode { get; set; } = string.Empty;
    /// <summary>The allowlist that was approved: hosts, ports, or "no external access".</summary>
    public List<string> AllowedHosts { get; set; } = [];
    public List<int> Ports { get; set; } = [];
    public bool AllowNone { get; set; }
}
