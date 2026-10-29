using System.Text.Json;
using System.Text.RegularExpressions;
using DevPilot.Core.Config;

namespace DevPilot.Core.Guard;

/// <summary>One scanner rule. Rules are data, so they can be extended without code changes.</summary>
public sealed class GuardRule
{
    public string Id { get; set; } = string.Empty;
    public GuardSeverity Severity { get; set; } = GuardSeverity.Warn;
    /// <summary>Regular expression matched against one line of text.</summary>
    public string Pattern { get; set; } = string.Empty;
    public bool CaseSensitive { get; set; }
    public string Message { get; set; } = string.Empty;
    /// <summary>Short "why it matters" text shown with the finding.</summary>
    public string Why { get; set; } = string.Empty;
    /// <summary>Short "how to fix" text shown with the finding.</summary>
    public string Fix { get; set; } = string.Empty;
}

/// <summary>A set of rules with their patterns compiled once. Rules with a broken pattern are skipped.</summary>
public sealed class GuardRuleSet
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    public IReadOnlyList<(GuardRule Rule, Regex Regex)> Compiled { get; }

    public GuardRuleSet(IEnumerable<GuardRule> rules)
    {
        var list = new List<(GuardRule, Regex)>();
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id) || string.IsNullOrWhiteSpace(rule.Pattern)) continue;
            try
            {
                var options = RegexOptions.CultureInvariant | (rule.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
                list.Add((rule, new Regex(rule.Pattern, options, MatchTimeout)));
            }
            catch (ArgumentException) { }
        }
        Compiled = list;
    }

    public static GuardRuleSet FromJson(string json) =>
        new(JsonSerializer.Deserialize<List<GuardRule>>(json, ConfigSerializer.Options) ?? []);

    public static GuardRuleSet Default { get; } = FromJson(DefaultRulesJson);

    internal const string DefaultRulesJson = """
    [
      { "id": "net-http", "severity": "Warn", "pattern": "\\b(HttpClient|WebClient|HttpWebRequest|XMLHttpRequest|Invoke-WebRequest|Invoke-RestMethod|aiohttp|httpx|urllib|axios)\\b|\\brequests\\.(get|post|put|patch|delete|session)\\b|\\bfetch\\s*\\(",
        "message": "HTTP client code", "why": "The tool can send data to web servers.", "fix": "Declare the hosts it needs in the network block of devpilot.tool.json, or remove the code if it is unused." },
      { "id": "net-socket", "severity": "Warn", "pattern": "\\b(TcpClient|UdpClient|WebSocket|ClientWebSocket|System\\.Net\\.Sockets)\\b|\\bsocket\\.socket\\b|\\bwebsockets?\\.connect\\b",
        "message": "Raw socket or WebSocket code", "why": "Sockets can carry any data to any address.", "fix": "Declare the hosts and ports in the network block, or remove the code." },
      { "id": "net-ftp-smtp", "severity": "Warn", "pattern": "\\b(FtpWebRequest|ftplib|SmtpClient|smtplib|Send-MailMessage)\\b",
        "message": "FTP or mail client code", "why": "Files or messages can leave the machine through these protocols.", "fix": "Remove it, or declare the server in the network block." },
      { "id": "hardcoded-url", "severity": "Info", "pattern": "https?://(?!localhost|127\\.0\\.0\\.1|example\\.|schemas\\.|www\\.w3\\.org|json-schema\\.org)[A-Za-z0-9.-]+",
        "message": "Hard-coded web address", "why": "Shows where the tool may connect.", "fix": "Add the host to the allowlist if it is expected; otherwise check why it is there." },
      { "id": "hardcoded-ip", "severity": "Info", "pattern": "\\b(?!127\\.|0\\.0\\.0\\.0)(?:\\d{1,3}\\.){3}\\d{1,3}\\b",
        "message": "Hard-coded IP address", "why": "A fixed address is a connection target (it may also be a version number).", "fix": "Check what it is; allowlist the host if it is a real target." },
      { "id": "telemetry", "severity": "Warn", "pattern": "\\b(Application ?Insights|Sentry|Mixpanel|Amplitude|Segment\\.Analytics|google-analytics|gtag|telemetry)\\b",
        "message": "Telemetry or analytics", "why": "Usage data may be sent to a third party.", "fix": "Remove it or switch it off; DevPilot passes DEVPILOT_SUPPRESS to managed tools." },
      { "id": "auto-update", "severity": "Warn", "pattern": "auto-?update|\\bAutoUpdater\\b|\\bSquirrel\\b|check[ _-]?for[ _-]?updates?",
        "message": "Auto-update code", "why": "The tool may download and run new code on its own.", "fix": "Disable it; update through git pull and review the change." },
      { "id": "upload", "severity": "Warn", "pattern": "\\b(UploadFile|UploadString|UploadData|upload_file|PutObject|BlobClient)\\b|multipart/form-data",
        "message": "File or data upload", "why": "Local files or data can leave the machine.", "fix": "Check what is uploaded and where; declare the host in the network block." },
      { "id": "capture-clipboard", "severity": "Warn", "pattern": "\\b(GetClipboardData|pyperclip|Get-Clipboard)\\b|Clipboard\\.(GetText|GetData|GetImage)",
        "message": "Clipboard access", "why": "Clipboard content can include copied secrets.", "fix": "Keep it local and never send it out; document why the tool reads it." },
      { "id": "capture-screen", "severity": "Warn", "pattern": "\\b(CopyFromScreen|ImageGrab|PrintWindow)\\b|pyautogui\\.screenshot|\\bmss\\.mss\\b",
        "message": "Screen capture", "why": "Screenshots can show confidential content.", "fix": "Keep captures local and never send them out." },
      { "id": "capture-keys", "severity": "High", "pattern": "\\b(SetWindowsHookEx|GetAsyncKeyState|WH_KEYBOARD(_LL)?)\\b|\\bpynput\\b|keyboard\\.(hook|on_press|add_hotkey)",
        "message": "Keyboard hook or key logging API", "why": "Can record everything typed, including passwords.", "fix": "Use RegisterHotKey for a single shortcut instead, or remove it." },
      { "id": "spawn-process", "severity": "Info", "pattern": "\\b(Process\\.Start|ShellExecute|Start-Process|child_process|os\\.system|subprocess\\.)|\\b(cmd|powershell|pwsh)(\\.exe)?\\s+/?-?[a-z]",
        "message": "Starts other programs", "why": "A started program can have its own network access.", "fix": "Check what it starts; DevPilot only monitors the process tree it launches." },
      { "id": "persistence", "severity": "Warn", "pattern": "CurrentVersion\\\\Run|\\bschtasks\\b|Register-ScheduledTask|shell:startup",
        "message": "Starts itself with Windows", "why": "DevPilot is the single owner of start-with-Windows.", "fix": "Remove it and honour DEVPILOT_SUPPRESS=autostart." },
      { "id": "ai-client", "severity": "Warn", "pattern": "api\\.openai\\.com|api\\.anthropic\\.com|generativelanguage\\.googleapis|\\bimport (openai|anthropic)\\b|\\b(OpenAI|Anthropic)Client\\b|\\bnew (OpenAI|Anthropic)\\(",
        "message": "AI service client", "why": "Text or files may be sent to an AI service.", "fix": "Use it only if AI use is allowed for the data involved; declare the host." },
      { "id": "secret", "severity": "High", "caseSensitive": true,
        "pattern": "AKIA[0-9A-Z]{16}|-----BEGIN ([A-Z]+ )?PRIVATE KEY-----|ghp_[A-Za-z0-9]{36}|sk-[A-Za-z0-9]{20,}|(?i:api[_-]?key|secret|password|token)\\s*[:=]\\s*[\"'][^\"']{8,}[\"']",
        "message": "Possible embedded secret", "why": "Secrets in files leak through git and backups.", "fix": "Move it to the keyring or an environment variable and rotate it." }
    ]
    """;
}
