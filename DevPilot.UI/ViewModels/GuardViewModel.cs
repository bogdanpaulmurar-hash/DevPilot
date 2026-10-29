using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using DevPilot.Core.Guard;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Launch;
using DevPilot.Infrastructure.Services;
using DevPilot.Models;
using DevPilot.UI.Hub;
using DevPilot.UI.Mvvm;
using DevPilot.UI.Services;
using Microsoft.Win32;

namespace DevPilot.UI.ViewModels;

public sealed class GuardFindingVM
{
    public GuardFinding Finding { get; }
    public GuardFindingVM(GuardFinding f) { Finding = f; }
    public string Title => $"{Finding.Severity} · {Finding.RuleId} · {Finding.File}:{Finding.Line}";
    public string Message => Finding.Message;
    public string Help => string.IsNullOrEmpty(Finding.Why) ? Finding.Fix : $"{Finding.Why} {Finding.Fix}";
}

public sealed class GuardToolRowVM : ObservableObject
{
    private GuardStatus _status = GuardStatus.Unreviewed;
    private bool _checked;
    private string _scanText = "Not scanned yet";
    private string _hosts = string.Empty;
    private string _ports = string.Empty;
    private bool _allowNone;
    private readonly IConfigStore _config;

    public ToolEntry Tool { get; }
    public GuardReport? Report { get; private set; }
    public ObservableCollection<GuardFindingVM> Findings { get; } = [];

    public GuardToolRowVM(ToolEntry tool, IConfigStore config)
    {
        Tool = tool;
        _config = config;
    }

    public string Name => Tool.Name;
    public string Glyph => FlyoutBuilder.GlyphOf(Tool);

    public GuardStatus Status { get => _status; private set { if (Set(ref _status, value)) { OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(Dot)); } } }

    public string StatusText => !_checked ? "Checking…" : _status switch
    {
        GuardStatus.Approved => "Approved",
        GuardStatus.Blocked => "Blocked",
        GuardStatus.Drifted => "Changed since approval, review again",
        _ => "Not reviewed",
    };

    public ToolRunState Dot => !_checked ? ToolRunState.Unknown : _status switch
    {
        GuardStatus.Approved => ToolRunState.Running,
        GuardStatus.Drifted => ToolRunState.Starting,
        GuardStatus.Blocked => ToolRunState.Failed,
        _ => ToolRunState.Unknown,
    };

    public string ScanText { get => _scanText; private set => Set(ref _scanText, value); }
    public bool HasFindings => Findings.Count > 0;

    public bool Offline
    {
        get => Tool.GuardOffline;
        set { if (Tool.GuardOffline == value) return; Tool.GuardOffline = value; OnPropertyChanged(); _config.Touch(ConfigArea.Tools); }
    }

    public string HostsText { get => _hosts; set => Set(ref _hosts, value); }
    public string PortsText { get => _ports; set => Set(ref _ports, value); }
    public bool AllowNone { get => _allowNone; set => Set(ref _allowNone, value); }

    public ToolNetworkPolicy Draft() => AllowlistText.Build(_hosts, _ports, _allowNone);

    public void LoadDraft(ToolNetworkPolicy? policy)
    {
        HostsText = AllowlistText.HostsToText(policy?.AllowedHosts ?? []);
        PortsText = AllowlistText.PortsToText(policy?.Ports ?? []);
        AllowNone = policy?.AllowNone ?? false;
    }

    public void SetStatus(GuardStatus status)
    {
        _checked = true;
        Status = status;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Dot));
    }

    public void SetReport(GuardReport report, string? note = null)
    {
        Report = report;
        Findings.Clear();
        foreach (var f in report.Findings.OrderByDescending(f => f.Severity)) Findings.Add(new GuardFindingVM(f));
        ScanText = note ?? (report.Findings.Count == 0
            ? "Scanned: nothing found"
            : $"Scanned: {report.Count(GuardSeverity.High)} high · {report.Count(GuardSeverity.Warn)} warn · {report.Count(GuardSeverity.Info)} info");
        OnPropertyChanged(nameof(HasFindings));
    }
}

public sealed class GuardViewModel : ObservableObject, IPage
{
    private readonly IConfigStore _config;
    private readonly KillSwitchService _killSwitch;
    private readonly GuardEventLog _events;

    public static IReadOnlyList<Choice<EgressMode>> Modes { get; } =
    [
        new(EgressMode.Off, "Off: no checks"),
        new(EgressMode.Monitor, "Monitor: warn and log (default)"),
        new(EgressMode.Block, "Block: only approved tools start; stop tools that leave their allowlist"),
    ];

    public ObservableCollection<GuardToolRowVM> Rows { get; } = [];
    public ObservableCollection<string> Recent { get; } = [];

    public ICommand ScanCommand { get; }
    public ICommand ScanAllCommand { get; }
    public ICommand ApproveCommand { get; }
    public ICommand BlockCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand PauseCommand { get; }

    public GuardViewModel(IConfigStore config, KillSwitchService killSwitch, GuardEventLog events)
    {
        _config = config;
        _killSwitch = killSwitch;
        _events = events;

        ScanCommand = new AsyncCommand(async p => { if (p is GuardToolRowVM r) await ScanAsync(r); });
        ScanAllCommand = new AsyncCommand(async () => { foreach (var r in Rows.ToList()) await ScanAsync(r); });
        ApproveCommand = new AsyncCommand(async p => { if (p is GuardToolRowVM r) await ApproveAsync(r); });
        BlockCommand = new RelayCommand(p =>
        {
            if (p is not GuardToolRowVM r) return;
            ApprovalGate.Block(r.Tool);
            _config.Touch(ConfigArea.Tools);
            r.SetStatus(GuardStatus.Blocked);
        });
        ResetCommand = new AsyncCommand(async p =>
        {
            if (p is not GuardToolRowVM r) return;
            ApprovalGate.Reset(r.Tool);
            _config.Touch(ConfigArea.Tools);
            await RefreshStatusAsync(r);
        });
        ExportCommand = new RelayCommand(Export);
        PauseCommand = new AsyncCommand(async () =>
        {
            if (_killSwitch.IsPaused) _killSwitch.Resume();
            else await _killSwitch.PauseAsync();
        });

        _killSwitch.Changed += (_, _) => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            OnPropertyChanged(nameof(IsPaused));
            OnPropertyChanged(nameof(PauseLabel));
        });
        _events.Added += (_, e) => Application.Current.Dispatcher.BeginInvoke(() => AddRecent(e));
    }

    public EgressMode Mode
    {
        get => _config.Current.Guard.Mode;
        set
        {
            if (_config.Current.Guard.Mode == value) return;
            _config.Current.Guard.Mode = value;
            _config.Touch(ConfigArea.Monitor);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ModeHint));
        }
    }

    public string ModeHint => Mode switch
    {
        EgressMode.Off => "Nothing is checked or logged.",
        EgressMode.Block => "A tool that is not approved (or changed since approval) does not start. A tool is stopped when it connects outside its allowlist. Tools without an allowlist are only logged.",
        _ => "Every tool starts. DevPilot warns about tools that are not approved and about connections outside an allowlist.",
    };

    public bool IsPaused => _killSwitch.IsPaused;
    public string PauseLabel => _killSwitch.IsPaused ? "Resume external access" : "Pause external access";

    public string PolicyStatus
    {
        get
        {
            if (!File.Exists(LocalPolicy.DefaultPath)) return "Local policy file: none (optional, stays on this PC and is never saved or exported).";
            return LocalPolicy.Load().LoadProblem is null
                ? "Local policy file: found and loaded (its contents are never shown, logged or exported)."
                : "Local policy file: found but unreadable, so it is ignored.";
        }
    }

    public void OnShown()
    {
        Rows.Clear();
        foreach (var tool in _config.Current.Tools.Where(ApprovalGate.Applies))
            Rows.Add(new GuardToolRowVM(tool, _config));
        Recent.Clear();
        foreach (var e in _events.Recent(50)) AddRecent(e);
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(ModeHint));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(PauseLabel));
        OnPropertyChanged(nameof(PolicyStatus));
        foreach (var row in Rows.ToList()) _ = RefreshStatusAsync(row, loadDraft: true);
    }

    private void AddRecent(GuardEvent e)
    {
        var what = e.Verdict == ConnectionVerdict.NotAllowed ? "outside allowlist" : "no allowlist declared";
        Recent.Insert(0, $"{e.TimeUtc.ToLocalTime():HH:mm:ss} · {e.ToolName} → {e.Host}:{e.Port} · {what}");
        while (Recent.Count > 50) Recent.RemoveAt(Recent.Count - 1);
    }

    private async Task RefreshStatusAsync(GuardToolRowVM row, bool loadDraft = false)
    {
        var tool = row.Tool;
        var (status, manifest) = await Task.Run(() =>
        {
            var provenance = ProvenanceReader.ReadForTool(tool);
            return (ApprovalGate.StatusOf(tool, provenance), GuardManifest.Network(tool));
        });
        row.SetStatus(status);
        if (loadDraft) row.LoadDraft(ApprovalGate.PolicyFor(tool, manifest));
    }

    private async Task ScanAsync(GuardToolRowVM row)
    {
        var dir = PathTokens.ToolDirectory(row.Tool);
        if (dir.Length == 0 || !Directory.Exists(dir))
        {
            row.SetReport(new GuardReport { ToolName = row.Name }, "Not scanned: the tool's folder was not found.");
            return;
        }
        if (ProvenanceReader.IsSharedFolder(dir))
        {
            row.SetReport(new GuardReport { ToolName = row.Name }, "Not scanned: the program sits in a shared folder (Windows, Program Files…).");
            return;
        }
        var report = await Task.Run(() => ToolScanner.Scan(dir, LocalPolicy.Load()));
        row.SetReport(new GuardReport { ToolName = row.Name, CreatedUtc = report.CreatedUtc, Findings = report.Findings });
    }

    private async Task ApproveAsync(GuardToolRowVM row)
    {
        var tool = row.Tool;
        var provenance = await Task.Run(() => ProvenanceReader.ReadForTool(tool));
        if (!ApprovalGate.Approve(tool, provenance, Mode, row.Draft(), DateTime.UtcNow))
        {
            Notifier.Error($"Could not approve {tool.Name}", "Its folder or program file was not found.");
            return;
        }
        _config.Touch(ConfigArea.Tools);
        row.SetStatus(GuardStatus.Approved);
    }

    private void Export()
    {
        var reports = Rows.Where(r => r.Report is not null).Select(r => r.Report!).ToList();
        if (reports.Count == 0)
        {
            Notifier.Info("Nothing to export", "Scan at least one tool first.");
            return;
        }
        var dlg = new SaveFileDialog { FileName = $"devpilot-guard-report-{DateTime.Now:yyyyMMdd}.json", Filter = "JSON (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, GuardReportExporter.ToJson(reports, DateTime.UtcNow));
            Notifier.Info("Report exported", "Rule ids, files, line numbers and counts only.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notifier.Error("Could not export the report", ex.Message);
        }
    }
}
