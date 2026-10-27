using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using DevPilot.Core.Config;
using DevPilot.Core.Help;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Presets;
using DevPilot.Models;
using DevPilot.UI.Hub;
using DevPilot.UI.Mvvm;
using DevPilot.UI.Services;
using Microsoft.Win32;

namespace DevPilot.UI.ViewModels;

public abstract class ListEntry : ObservableObject
{
    public abstract string Id { get; }
}

public sealed class GroupListEntry(ToolGroup group, Func<int> count) : ListEntry
{
    public ToolGroup Group { get; } = group;
    public override string Id => Group.Id;
    public string Title => Group.Title;
    public string Glyph => Group.Glyph;
    public string Color => Group.Color;
    public bool HasColor => !string.IsNullOrWhiteSpace(Group.Color);
    public string CountText => count().ToString();
    public bool IsHidden => Group.Hidden;
}

public sealed class ToolListEntry(ToolEntry tool) : ListEntry
{
    public ToolEntry Tool { get; } = tool;
    public override string Id => Tool.Id;
    public string Name => Tool.IsSeparator
        ? (string.IsNullOrWhiteSpace(Tool.Name) ? "──────  separator" : $"──  {Tool.Name}")
        : string.IsNullOrWhiteSpace(Tool.Name) ? "(unnamed)" : Tool.Name;
    public string Glyph => Tool.IsSeparator ? string.Empty : FlyoutBuilder.GlyphOf(Tool);
    public bool IsSeparator => Tool.IsSeparator;
    public string Color => Tool.Color;
    public bool HasColor => !string.IsNullOrWhiteSpace(Tool.Color);
    /// <summary>Name colour in the list (as chosen; the hub additionally checks readability).</summary>
    public string TextColor => Tool.TextColor;
    public bool HasTextColor => !string.IsNullOrWhiteSpace(Tool.TextColor);
    public bool IsHidden => Tool.Hidden;
    public bool IsSpotlight => Tool.Spotlight;
    public string Badge => Tool.Tray switch
    {
        TrayMode.DevPilotIcon => "own icon",
        TrayMode.Native => "native tray",
        _ => string.Empty,
    };
}

public sealed record Choice<T>(T Value, string Label);

/// <summary>Tools &amp; groups editor: one list of groups with their tools, and a property panel.</summary>
public sealed class ToolsViewModel : ObservableObject, IPage
{
    private readonly IConfigStore _config;
    private readonly ILauncher _launcher;
    private readonly IProcessMonitor _monitor;
    private ListEntry? _selected;
    private object? _editor;

    public ObservableCollection<ListEntry> Items { get; } = [];
    public IReadOnlyList<ToolPreset> Presets => ToolPresets.All;

    public ICommand AddToolCommand { get; }
    public ICommand AddFolderCommand { get; }
    public ICommand AddLinkCommand { get; }
    public ICommand AddPresetCommand { get; }
    public ICommand AddGroupCommand { get; }
    public ICommand AddSeparatorCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand DuplicateCommand { get; }

    public ToolsViewModel(IConfigStore config, ILauncher launcher, IProcessMonitor monitor)
    {
        _config = config;
        _launcher = launcher;
        _monitor = monitor;

        AddToolCommand = new RelayCommand(() => AddTool(new ToolEntry { Name = "New tool", Kind = ToolKind.Executable }));
        AddFolderCommand = new RelayCommand(AddFolder);
        AddLinkCommand = new RelayCommand(() => AddTool(new ToolEntry { Name = "New link", Kind = ToolKind.Url, Path = "https://" }));
        AddPresetCommand = new RelayCommand(p => { if (p is ToolPreset preset) AddPreset(preset); });
        AddGroupCommand = new RelayCommand(AddGroup);
        AddSeparatorCommand = new RelayCommand(() => AddTool(new ToolEntry
        {
            Name = string.Empty,
            Kind = ToolKind.Separator,
            Tray = TrayMode.HubOnly,
            Managed = new ManagedOptions { Enabled = false },
        }));
        MoveUpCommand = new RelayCommand(() => Move(-1), () => _selected is not null);
        MoveDownCommand = new RelayCommand(() => Move(1), () => _selected is not null);
        DeleteCommand = new RelayCommand(Delete, () => _selected is not null);
        DuplicateCommand = new RelayCommand(Duplicate, () => _selected is ToolListEntry);

        _config.Changed += (_, area) =>
        {
            if (area == ConfigArea.All) Application.Current.Dispatcher.BeginInvoke(() => Rebuild(_selected?.Id));
            else if (area is ConfigArea.Tools or ConfigArea.Groups) Application.Current.Dispatcher.BeginInvoke(RefreshEntries);
        };
        Rebuild(null);
    }

    private AppSettings S => _config.Current;

    public ListEntry? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            using (EditTracker.Suppress())
            {
                Editor = value switch
                {
                    ToolListEntry { Tool.IsSeparator: true } sep => new SeparatorEditorVM(sep.Tool, this, _config),
                    ToolListEntry t => new ToolEditorVM(t.Tool, this, _config, _launcher, _monitor),
                    GroupListEntry g => new GroupEditorVM(g.Group, this),
                    _ => null,
                };
            }
        }
    }

    public object? Editor { get => _editor; private set => Set(ref _editor, value); }
    public bool IsEmpty => Items.Count == 0;

    public void OnShown() => RefreshEntries();

    public void Rebuild(string? selectId)
    {
        Items.Clear();
        foreach (var g in S.Groups.OrderBy(g => g.Order))
        {
            var group = g;
            Items.Add(new GroupListEntry(group, () => S.Tools.Count(t => t.GroupId == group.Id)));
            foreach (var t in Ordering.ToolsOf(S, g.Id)) Items.Add(new ToolListEntry(t));
        }
        var target = Items.FirstOrDefault(i => i.Id == selectId) ?? Items.OfType<ToolListEntry>().FirstOrDefault() ?? Items.FirstOrDefault();
        _selected = null;
        Selected = target;
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Names, glyphs and badges changed in the editor.</summary>
    public void RefreshEntries()
    {
        foreach (var i in Items) i.RefreshAll();
    }

    public void Select(ToolEntry tool) => Selected = Items.FirstOrDefault(i => i.Id == tool.Id) ?? Selected;

    private string TargetGroupId()
    {
        if (_selected is GroupListEntry g) return g.Group.Id;
        if (_selected is ToolListEntry t) return t.Tool.GroupId;
        if (S.Groups.Count == 0)
        {
            var grp = new ToolGroup { Title = "My tools" };
            S.Groups.Add(grp);
        }
        return S.Groups.OrderBy(x => x.Order).First().Id;
    }

    private void AddTool(ToolEntry tool)
    {
        tool.GroupId = TargetGroupId();
        tool.Order = int.MaxValue;
        S.Tools.Add(tool);
        Ordering.Renumber(S);
        _config.Touch(ConfigArea.Tools);
        Rebuild(tool.Id);
    }

    private void AddFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Choose a folder to add" };
        if (dlg.ShowDialog() != true) return;
        AddTool(new ToolEntry
        {
            Name = Path.GetFileName(dlg.FolderName.TrimEnd('\\')) is { Length: > 0 } n ? n : dlg.FolderName,
            Kind = ToolKind.Folder,
            Path = dlg.FolderName,
            Folder = new FolderOptions { ExpandInHub = true },
        });
    }

    private void AddPreset(ToolPreset preset)
    {
        string path = string.Empty;
        bool moduleBased = preset.Kind == ToolKind.Python && !string.IsNullOrEmpty(preset.PythonModule);
        if (moduleBased)
        {
            var dlg = new OpenFolderDialog { Title = $"{preset.Name}: choose the project folder (the one that contains the '{preset.PythonModule}' package or 'src')" };
            if (dlg.ShowDialog() == true) path = dlg.FolderName;
            var src = Path.Combine(path, "src");
            if (path.Length > 0 && Directory.Exists(Path.Combine(src, preset.PythonModule))) path = src;
        }
        else
        {
            var dlg = new OpenFileDialog
            {
                Title = $"{preset.Name}: choose the file that starts it",
                Filter = preset.Kind == ToolKind.Python
                    ? "Python (*.py;*.pyw)|*.py;*.pyw|All files (*.*)|*.*"
                    : "Programs (*.exe)|*.exe|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog() == true) path = dlg.FileName;
        }
        var tool = ToolPresets.Create(preset, path);
        AddTool(tool);
    }

    private void AddGroup()
    {
        var g = new ToolGroup { Title = "New group", Order = S.Groups.Count };
        S.Groups.Add(g);
        _config.Touch(ConfigArea.Groups);
        Rebuild(g.Id);
    }

    private void Move(int delta)
    {
        switch (_selected)
        {
            case ToolListEntry t:
                if (!Ordering.MoveTool(S, t.Tool, delta))
                {
                    // At the edge of its group: move into the neighbouring group.
                    var groups = S.Groups.OrderBy(g => g.Order).ToList();
                    int gi = groups.FindIndex(g => g.Id == t.Tool.GroupId) + delta;
                    if (gi < 0 || gi >= groups.Count) return;
                    Ordering.MoveToGroup(S, t.Tool, groups[gi].Id, delta < 0 ? int.MaxValue : 0);
                }
                _config.Touch(ConfigArea.Tools);
                Rebuild(t.Id);
                break;
            case GroupListEntry g:
                if (!Ordering.MoveGroup(S, g.Group, delta)) return;
                _config.Touch(ConfigArea.Groups);
                Rebuild(g.Id);
                break;
        }
    }

    public void MoveToGroup(ToolEntry tool, string groupId)
    {
        if (tool.GroupId == groupId) return;
        Ordering.MoveToGroup(S, tool, groupId);
        _config.Touch(ConfigArea.Tools);
        Rebuild(tool.Id);
    }

    private void Delete()
    {
        switch (_selected)
        {
            case ToolListEntry t:
                if (MessageBox.Show($"Remove \"{t.Tool.Name}\" from DevPilot?\nNothing is deleted from disk.", "DevPilot",
                        MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                int idx = Items.IndexOf(t);
                S.Tools.Remove(t.Tool);
                Ordering.Renumber(S);
                _config.Touch(ConfigArea.Tools);
                Rebuild(idx > 0 ? Items[idx - 1].Id : null);
                break;
            case GroupListEntry g:
                var tools = S.Tools.Where(x => x.GroupId == g.Group.Id).ToList();
                var other = S.Groups.Where(x => x.Id != g.Group.Id).OrderBy(x => x.Order).FirstOrDefault();
                if (tools.Count > 0 && other is null)
                {
                    MessageBox.Show("This is the last group. Add another group first, or remove its tools.", "DevPilot");
                    return;
                }
                var msg = tools.Count == 0
                    ? $"Delete the group \"{g.Group.Title}\"?"
                    : $"Delete the group \"{g.Group.Title}\"? Its {tools.Count} tool(s) move to \"{other!.Title}\".";
                if (MessageBox.Show(msg, "DevPilot", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                foreach (var t in tools) Ordering.MoveToGroup(S, t, other!.Id);
                S.Groups.Remove(g.Group);
                Ordering.Renumber(S);
                _config.Touch(ConfigArea.Groups);
                Rebuild(other?.Id);
                break;
        }
    }

    private void Duplicate()
    {
        if (_selected is not ToolListEntry t) return;
        var json = System.Text.Json.JsonSerializer.Serialize(t.Tool, ConfigSerializer.Options);
        var copy = System.Text.Json.JsonSerializer.Deserialize<ToolEntry>(json, ConfigSerializer.Options)!;
        copy.Id = NewId.Short();
        if (!copy.IsSeparator) copy.Name += " (copy)";
        copy.Spotlight = false;
        copy.AutoStart.Enabled = false;
        foreach (var a in copy.Actions) a.Id = NewId.Short();
        copy.Order = t.Tool.Order + 1;
        foreach (var other in S.Tools.Where(x => x.GroupId == copy.GroupId && x.Order > t.Tool.Order)) other.Order++;
        S.Tools.Add(copy);
        Ordering.Renumber(S);
        _config.Touch(ConfigArea.Tools);
        Rebuild(copy.Id);
    }

    public IReadOnlyList<ToolGroup> Groups => S.Groups.OrderBy(g => g.Order).ToList();
}

public sealed class ActionEditorVM : ObservableObject
{
    public ToolAction Model { get; }
    public ActionEditorVM(ToolAction model) => Model = model;
    public bool IsSeparator => Model.Kind == ActionKind.Separator;
    public bool IsNotSeparator => !IsSeparator;

    public ActionKind Kind
    {
        get => Model.Kind;
        set
        {
            if (Model.Kind == value) return;
            Model.Kind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ValueHint));
            OnPropertyChanged(nameof(ShowValue));
        }
    }

    public bool ShowValue => Model.Kind is not ActionKind.Arguments;
    public string ValueHint => Model.Kind switch
    {
        ActionKind.Url => "https://… (or http://127.0.0.1:port/…)",
        ActionKind.Folder => @"C:\… or {ToolDir}\…",
        ActionKind.File => @"File to open, e.g. {ToolDir}\config.json",
        ActionKind.Command => "Program, e.g. notepad.exe",
        _ => string.Empty,
    };
    public string DisplayGlyph => string.IsNullOrWhiteSpace(Model.Glyph) ? Glyphs.ForAction(Model.Kind) : Model.Glyph;
    public void NotifyGlyph() => OnPropertyChanged(nameof(DisplayGlyph));
}

public sealed class ToolEditorVM : ObservableObject
{
    private readonly ToolsViewModel _owner;
    private readonly IConfigStore _config;
    private readonly ILauncher _launcher;
    private readonly IProcessMonitor _monitor;
    private string _processInfo = string.Empty;

    public ToolEntry Model { get; }
    public ObservableCollection<ActionEditorVM> Actions { get; } = [];

    public static IReadOnlyList<Choice<ToolKind>> Kinds { get; } =
    [
        new(ToolKind.Executable, "Program (.exe)"),
        new(ToolKind.Python, "Python script or module"),
        new(ToolKind.PowerShell, "PowerShell script"),
        new(ToolKind.Batch, "Batch file (.bat / .cmd)"),
        new(ToolKind.VBScript, "VBScript (.vbs)"),
        new(ToolKind.ShellCommand, "Command line"),
        new(ToolKind.Folder, "Folder"),
        new(ToolKind.NetworkShare, "Network folder"),
        new(ToolKind.Url, "Web link"),
        new(ToolKind.Document, "Document / file"),
        new(ToolKind.Ssh, "SSH connection"),
        new(ToolKind.Wsl, "WSL command"),
    ];

    public static IReadOnlyList<Choice<ActionKind>> ActionKinds { get; } =
    [
        new(ActionKind.Arguments, "Run the tool with arguments"),
        new(ActionKind.Command, "Run a command"),
        new(ActionKind.Url, "Open a web page"),
        new(ActionKind.Folder, "Open a folder"),
        new(ActionKind.File, "Open a file"),
    ];

    public static IReadOnlyList<GlyphInfo> GlyphCatalogue => Glyphs.Catalogue;
    public static IReadOnlyList<string> Palette { get; } =
        ["#4F8EF7", "#2BB3C0", "#3FB28C", "#8FB339", "#D9A441", "#E07A3F", "#E5534B", "#E07AB5", "#A371F7", "#8B949E"];

    public ICommand BrowsePathCommand { get; }
    public ICommand BrowseWorkDirCommand { get; }
    public ICommand BrowseInterpreterCommand { get; }
    public ICommand BrowseIconCommand { get; }
    public ICommand ClearIconCommand { get; }
    public ICommand SetGlyphCommand { get; }
    public ICommand SetColorCommand { get; }
    public ICommand AddActionCommand { get; }
    public ICommand AddSeparatorCommand { get; }
    public ICommand RemoveActionCommand { get; }
    public ICommand ActionUpCommand { get; }
    public ICommand ActionDownCommand { get; }
    public ICommand TestActionCommand { get; }
    public ICommand ResetActionsCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand DetectCommand { get; }
    public ICommand ApplyPresetCommand { get; }
    public ICommand LoadManifestCommand { get; }

    public ToolEditorVM(ToolEntry model, ToolsViewModel owner, IConfigStore config, ILauncher launcher, IProcessMonitor monitor)
    {
        Model = model;
        _owner = owner;
        _config = config;
        _launcher = launcher;
        _monitor = monitor;
        foreach (var a in model.Actions) Actions.Add(new ActionEditorVM(a));

        BrowsePathCommand = new RelayCommand(BrowsePath);
        BrowseWorkDirCommand = new RelayCommand(() =>
        {
            var dlg = new OpenFolderDialog { Title = "Working folder" };
            if (dlg.ShowDialog() == true) { Model.WorkingDirectory = dlg.FolderName; Changed(nameof(WorkingDirectory)); }
        });
        BrowseInterpreterCommand = new RelayCommand(() =>
        {
            var dlg = new OpenFileDialog { Title = "Python interpreter (pythonw.exe of a venv, for example)", Filter = "Python (python*.exe;pythonw*.exe;py*.exe)|python*.exe;pythonw*.exe;py*.exe|All files|*.*" };
            if (dlg.ShowDialog() == true) { Model.Interpreter = dlg.FileName; Changed(nameof(Interpreter)); }
        });
        BrowseIconCommand = new RelayCommand(() =>
        {
            var dlg = new OpenFileDialog
            {
                Title = "Logo (hub and tray icon)",
                Filter = "Logos and programs (*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.exe;*.dll;*.lnk)|*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.exe;*.dll;*.lnk|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog() == true) IconPath = dlg.FileName;
        });
        ClearIconCommand = new RelayCommand(() => IconPath = string.Empty);
        SetGlyphCommand = new RelayCommand(p => { Glyph = p as string ?? string.Empty; });
        SetColorCommand = new RelayCommand(p => { Color = p as string ?? string.Empty; });
        AddActionCommand = new RelayCommand(() => AddAction(new ToolAction { Label = "New action", Kind = ActionKind.Arguments }));
        AddSeparatorCommand = new RelayCommand(() => AddAction(new ToolAction { Kind = ActionKind.Separator, Label = "—" }));
        RemoveActionCommand = new RelayCommand(p =>
        {
            if (p is not ActionEditorVM a) return;
            Model.Actions.Remove(a.Model);
            Actions.Remove(a);
            Touch();
        });
        ActionUpCommand = new RelayCommand(p => MoveAction(p as ActionEditorVM, -1));
        ActionDownCommand = new RelayCommand(p => MoveAction(p as ActionEditorVM, 1));
        TestActionCommand = new AsyncCommand(async p =>
        {
            if (p is ActionEditorVM a && !a.IsSeparator)
                await FlyoutBuilder.Run(_launcher.RunActionAsync(Model, a.Model), a.Model.Label);
        });
        ResetActionsCommand = new RelayCommand(() =>
        {
            var preset = ToolPresets.Recognise(Model);
            if (preset is null) return;
            if (MessageBox.Show($"Replace the actions with the {preset.Name} defaults?", "DevPilot", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
            Model.Actions = preset.Actions();
            Actions.Clear();
            foreach (var a in Model.Actions) Actions.Add(new ActionEditorVM(a));
            Touch();
        });
        StartCommand = new AsyncCommand(async () => { await FlyoutBuilder.Run(_launcher.StartAsync(Model), Model.Name); await DetectAsync(); });
        StopCommand = new AsyncCommand(async () => { await _launcher.StopAsync(Model); await DetectAsync(); });
        DetectCommand = new AsyncCommand(DetectAsync);
        LoadManifestCommand = new RelayCommand(() =>
        {
            var path = ToolManifest.Find(Model);
            if (path is null)
            {
                var dlg = new OpenFileDialog { Title = "devpilot.tool.json", Filter = "DevPilot manifest (devpilot.tool.json)|devpilot.tool.json;*.json" };
                if (dlg.ShowDialog() != true) return;
                path = dlg.FileName;
            }
            try
            {
                ToolManifest.Load(path).ApplyTo(Model, overwrite: true);
                Actions.Clear();
                foreach (var a in Model.Actions) Actions.Add(new ActionEditorVM(a));
                RefreshAll();
                Touch();
            }
            catch (Exception ex) { Notifier.Error("devpilot.tool.json", ex.Message); }
        });
        ApplyPresetCommand = new RelayCommand(p =>
        {
            if (p is not ToolPreset preset) return;
            ToolPresets.Apply(Model, preset, overwriteUserFields: false);
            Actions.Clear();
            foreach (var a in Model.Actions) Actions.Add(new ActionEditorVM(a));
            RefreshAll();
            Touch();
        });
    }

    private void Touch()
    {
        _config.Touch(ConfigArea.Tools);
        _owner.RefreshEntries();
    }

    private void Changed(string property)
    {
        OnPropertyChanged(property);
        Touch();
    }

    // Properties that drive visibility or need change notification.
    public ToolKind Kind
    {
        get => Model.Kind;
        set
        {
            if (Model.Kind == value) return;
            LogoCache.Shared.Forget(Model);
            Model.Kind = value;
            if (value == ToolKind.Python && string.IsNullOrEmpty(Model.Interpreter)) Model.HideWindow = true;
            RefreshAll();
            Touch();
        }
    }

    public bool IsPython => Model.Kind == ToolKind.Python;
    public IReadOnlyList<HelpTopic> KindHelp => ToolHelp.ForKind(Model.Kind);
    public IReadOnlyList<HelpTopic> CommonHelp => ToolHelp.Common;
    public bool IsFolder => Model.IsFolderLike;
    public bool IsProcess => Model.IsProcessLike;
    public bool IsConsoleKind => Model.Kind is ToolKind.Python or ToolKind.PowerShell or ToolKind.Batch or ToolKind.ShellCommand or ToolKind.Executable;
    public bool HasArguments => Model.Kind is not ToolKind.Url and not ToolKind.Folder and not ToolKind.NetworkShare;
    public bool HasWorkingDirectory => Model.Kind is not ToolKind.Url and not ToolKind.Folder and not ToolKind.NetworkShare;
    public string PathLabel => Model.Kind switch
    {
        ToolKind.Executable => "Program",
        ToolKind.Python => "Script (.py / .pyw) — leave empty when using a module",
        ToolKind.Folder or ToolKind.NetworkShare => "Folder",
        ToolKind.Url => "Address",
        ToolKind.Document => "File",
        ToolKind.ShellCommand => "Command",
        ToolKind.Ssh => "Host (user@server)",
        ToolKind.Wsl => "Command",
        _ => "Script",
    };

    public string Path
    {
        get => Model.Path;
        set { if (Model.Path == value) return; LogoCache.Shared.Forget(Model); Model.Path = value; OnPropertyChanged(); OnPropertyChanged(nameof(Preview)); }
    }
    public string WorkingDirectory { get => Model.WorkingDirectory; set { Model.WorkingDirectory = value; OnPropertyChanged(); } }
    public string Interpreter { get => Model.Interpreter; set { Model.Interpreter = value; OnPropertyChanged(); } }
    /// <summary>The logo file (hub and separate tray icon). Empty = manifest icon, else the program's own icon.</summary>
    public string IconPath
    {
        get => Model.IconPath;
        set
        {
            value = (value ?? string.Empty).Trim();
            if (Model.IconPath == value) return;
            LogoCache.Shared.Forget(Model);
            Model.IconPath = value;
            LogoCache.Shared.Forget(Model);
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasIconPath));
            LookChanged();
        }
    }
    public bool HasIconPath => !string.IsNullOrWhiteSpace(Model.IconPath);

    public static IReadOnlyList<Choice<HubLabelMode>> LabelModes => RowLook.LabelModes;

    public HubLabelMode LabelMode
    {
        get => Model.LabelMode;
        set { if (Model.LabelMode == value) return; Model.LabelMode = value; OnPropertyChanged(); LookChanged(); }
    }

    public string Glyph
    {
        get => Model.Glyph;
        set
        {
            var parsed = Glyphs.Parse(value);
            if (Model.Glyph == parsed) return;
            Model.Glyph = parsed;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayGlyph));
            OnPropertyChanged(nameof(Preview));
            Touch();
        }
    }
    public string DisplayGlyph => FlyoutBuilder.GlyphOf(Model);

    public string Color
    {
        get => Model.Color;
        set
        {
            var c = ConfigMigrator.NormalizeColor(value);
            if (Model.Color == c) return;
            Model.Color = c;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasColor));
            OnPropertyChanged(nameof(Preview));
            Touch();
        }
    }
    public bool HasColor => !string.IsNullOrWhiteSpace(Model.Color);

    public static IReadOnlyList<Choice<RowColorStyle>> ColorStyles => RowLook.Styles;

    public RowColorStyle ColorStyle
    {
        get => Model.ColorStyle;
        set { if (Model.ColorStyle == value) return; Model.ColorStyle = value; OnPropertyChanged(); LookChanged(); }
    }

    public string TextColor
    {
        get => Model.TextColor;
        set { var c = ConfigMigrator.NormalizeColor(value); if (Model.TextColor == c) return; Model.TextColor = c; OnPropertyChanged(); LookChanged(); }
    }

    public string GlyphColor
    {
        get => Model.GlyphColor;
        set { var c = ConfigMigrator.NormalizeColor(value); if (Model.GlyphColor == c) return; Model.GlyphColor = c; OnPropertyChanged(); LookChanged(); }
    }

    public bool Bold { get => Model.Bold; set { if (Model.Bold == value) return; Model.Bold = value; OnPropertyChanged(); LookChanged(); } }
    public bool Italic { get => Model.Italic; set { if (Model.Italic == value) return; Model.Italic = value; OnPropertyChanged(); LookChanged(); } }

    /// <summary>The row exactly as the hub will draw it (colours, readability fallback, tint).</summary>
    public HubToolVM Preview => new(Model, _config.Current.Hub, false);

    private void LookChanged()
    {
        OnPropertyChanged(nameof(Preview));
        Touch();
    }

    public TrayMode Tray
    {
        get => Model.Tray;
        set { if (Model.Tray == value) return; Model.Tray = value; OnPropertyChanged(); OnPropertyChanged(nameof(SuppressText)); OnPropertyChanged(nameof(IsDevPilotIcon)); Touch(); }
    }
    public bool IsDevPilotIcon => Model.Tray == TrayMode.DevPilotIcon;

    public string GroupId
    {
        get => Model.GroupId;
        set { if (value is not null && value != Model.GroupId) _owner.MoveToGroup(Model, value); }
    }
    public IReadOnlyList<ToolGroup> Groups => _owner.Groups;

    public ToolPreset? Preset => ToolPresets.Recognise(Model);
    public bool HasPreset => Preset is not null;
    public string PresetNotes => Preset?.Notes ?? string.Empty;
    public string PresetSupport => Preset is null ? string.Empty
        : Preset.SupportsSuppressEnv
            ? $"{Preset.Name} already understands DevPilot's switches."
            : $"{Preset.Name} does not read DEVPILOT_SUPPRESS yet: its own tray icon and keep-awake stay until it is updated (see docs/INTEGRATION.md). Autostart is handled from the Startup page either way.";
    public bool CanResetActions => Preset is not null;
    public IReadOnlyList<ToolPreset> AllPresets => ToolPresets.All;

    public string SuppressText => $"Sent to the tool: DEVPILOT_MANAGED=1, DEVPILOT_SUPPRESS={Core.Launch.LaunchPlanner.SuppressList(Model)}";

    public string ProcessInfo { get => _processInfo; private set => Set(ref _processInfo, value); }

    private async Task DetectAsync()
    {
        if (!Core.Launch.ProcessMatcher.CanTrack(Model))
        {
            ProcessInfo = "DevPilot cannot recognise this tool's process (set a process match below).";
            return;
        }
        var pids = await Task.Run(() => _monitor.FindProcesses(Model));
        var patterns = string.Join("; ", Core.Launch.ProcessMatcher.Patterns(Model));
        ProcessInfo = pids.Count == 0
            ? $"Not running (matching: {patterns})"
            : $"Running: {string.Join(", ", pids.Select(p => "PID " + p))} (matching: {patterns})";
    }

    private void BrowsePath()
    {
        if (Model.IsFolderLike)
        {
            var dlg = new OpenFolderDialog { Title = "Folder" };
            if (dlg.ShowDialog() == true) { Model.Path = dlg.FolderName; Changed(nameof(Path)); }
            return;
        }
        var filter = Model.Kind switch
        {
            ToolKind.Executable => "Programs (*.exe)|*.exe|All files (*.*)|*.*",
            ToolKind.Python => "Python (*.py;*.pyw)|*.py;*.pyw|All files (*.*)|*.*",
            ToolKind.PowerShell => "PowerShell (*.ps1)|*.ps1|All files (*.*)|*.*",
            ToolKind.Batch => "Batch (*.bat;*.cmd)|*.bat;*.cmd|All files (*.*)|*.*",
            ToolKind.VBScript => "VBScript (*.vbs)|*.vbs|All files (*.*)|*.*",
            _ => "All files (*.*)|*.*",
        };
        var file = new OpenFileDialog { Title = "Choose", Filter = filter };
        if (file.ShowDialog() != true) return;
        Model.Path = file.FileName;
        if (string.IsNullOrWhiteSpace(Model.Name) || Model.Name is "New tool")
            Model.Name = System.IO.Path.GetFileNameWithoutExtension(file.FileName);
        ToolPresets.ApplyIfRecognised(Model, overwriteUserFields: false);
        if (ToolManifest.Find(Model) is { } manifest && Model.Actions.Count == 0)
        {
            try { ToolManifest.Load(manifest).ApplyTo(Model); }
            catch (Exception ex) { Notifier.Error("devpilot.tool.json", ex.Message); }
        }
        Actions.Clear();
        foreach (var a in Model.Actions) Actions.Add(new ActionEditorVM(a));
        RefreshAll();
        Touch();
    }

    private void AddAction(ToolAction action)
    {
        Model.Actions.Add(action);
        Actions.Add(new ActionEditorVM(action));
        Touch();
    }

    private void MoveAction(ActionEditorVM? a, int delta)
    {
        if (a is null || !Ordering.MoveAction(Model, a.Model, delta)) return;
        int i = Actions.IndexOf(a);
        Actions.Move(i, i + delta);
        Touch();
    }
}

public sealed class GroupEditorVM : ObservableObject
{
    private readonly ToolsViewModel _owner;
    public ToolGroup Model { get; }
    public ICommand SetGlyphCommand { get; }
    public ICommand SetColorCommand { get; }

    public GroupEditorVM(ToolGroup model, ToolsViewModel owner)
    {
        Model = model;
        _owner = owner;
        SetGlyphCommand = new RelayCommand(p => Glyph = p as string ?? string.Empty);
        SetColorCommand = new RelayCommand(p => Color = p as string ?? string.Empty);
    }

    public string Glyph
    {
        get => Model.Glyph;
        set { Model.Glyph = Glyphs.Parse(value); OnPropertyChanged(); Notify(); }
    }

    public string Color
    {
        get => Model.Color;
        set { Model.Color = ConfigMigrator.NormalizeColor(value); OnPropertyChanged(); Notify(); }
    }

    public static IReadOnlyList<Choice<RowColorStyle>> ColorStyles => RowLook.Styles;

    public RowColorStyle ColorStyle
    {
        get => Model.ColorStyle;
        set { if (Model.ColorStyle == value) return; Model.ColorStyle = value; OnPropertyChanged(); Notify(); }
    }

    public string TextColor
    {
        get => Model.TextColor;
        set { Model.TextColor = ConfigMigrator.NormalizeColor(value); OnPropertyChanged(); Notify(); }
    }

    public string GlyphColor
    {
        get => Model.GlyphColor;
        set { Model.GlyphColor = ConfigMigrator.NormalizeColor(value); OnPropertyChanged(); Notify(); }
    }

    /// <summary>The group header exactly as the hub will draw it.</summary>
    public HubGroupVM Preview
    {
        get
        {
            var config = App.GetService<IConfigStore>();
            return new HubGroupVM(Model, config.Current.Hub, config);
        }
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Preview));
        App.GetService<IConfigStore>().Touch(ConfigArea.Groups);
        _owner.RefreshEntries();
    }

    public static IReadOnlyList<GlyphInfo> GlyphCatalogue => Glyphs.Catalogue;
    public static IReadOnlyList<string> Palette => ToolEditorVM.Palette;
    public int ToolCount => App.GetService<IConfigStore>().Current.Tools.Count(t => t.GroupId == Model.Id);
}

/// <summary>Shared choices for row colour styles.</summary>
public static class RowLook
{
    public static IReadOnlyList<Choice<RowColorStyle>> Styles { get; } =
    [
        new(RowColorStyle.Marker, "Bar on the left"),
        new(RowColorStyle.Highlight, "Highlight the whole row"),
        new(RowColorStyle.MarkerAndHighlight, "Bar and highlight"),
    ];

    /// <summary>Per entry (first choice follows the hub setting).</summary>
    public static IReadOnlyList<Choice<HubLabelMode>> LabelModes { get; } =
    [
        new(HubLabelMode.Default, "Like the hub (Appearance)"),
        new(HubLabelMode.Auto, "Logo and name, symbol when there is no logo"),
        new(HubLabelMode.LogoAndText, "Logo and name"),
        new(HubLabelMode.LogoOnly, "Logo only (name in the tooltip)"),
        new(HubLabelMode.TextAndSymbol, "Symbol and name"),
        new(HubLabelMode.TextOnly, "Name only"),
    ];

    /// <summary>Hub-wide default (Appearance).</summary>
    public static IReadOnlyList<Choice<HubLabelMode>> HubLabelModes { get; } = LabelModes.Skip(1).ToList();

    public static IReadOnlyList<Choice<SeparatorStyle>> SeparatorStyles { get; } =
    [
        new(SeparatorStyle.Line, "Line"),
        new(SeparatorStyle.Label, "Caption and line"),
        new(SeparatorStyle.Space, "Empty space"),
    ];
}

/// <summary>Editor for a separator row: caption, style, colours and group.</summary>
public sealed class SeparatorEditorVM : ObservableObject
{
    private readonly ToolsViewModel _owner;
    private readonly IConfigStore _config;

    public ToolEntry Model { get; }

    public SeparatorEditorVM(ToolEntry model, ToolsViewModel owner, IConfigStore config)
    {
        Model = model;
        _owner = owner;
        _config = config;
    }

    public static IReadOnlyList<Choice<SeparatorStyle>> Styles => RowLook.SeparatorStyles;

    public string Caption
    {
        get => Model.Name;
        set { if (Model.Name == (value ?? string.Empty)) return; Model.Name = value ?? string.Empty; OnPropertyChanged(); Changed(); }
    }

    public SeparatorStyle Style
    {
        get => Model.SeparatorStyle;
        set
        {
            if (Model.SeparatorStyle == value) return;
            Model.SeparatorStyle = value;
            // A caption only shows in the "Caption and line" style.
            if (value == SeparatorStyle.Label && string.IsNullOrWhiteSpace(Model.Name)) Model.Name = "Section";
            OnPropertyChanged();
            OnPropertyChanged(nameof(Caption));
            OnPropertyChanged(nameof(HasCaption));
            Changed();
        }
    }

    public bool HasCaption => Model.SeparatorStyle == SeparatorStyle.Label;

    public string Color
    {
        get => Model.Color;
        set { var c = ConfigMigrator.NormalizeColor(value); if (Model.Color == c) return; Model.Color = c; OnPropertyChanged(); Changed(); }
    }

    public string TextColor
    {
        get => Model.TextColor;
        set { var c = ConfigMigrator.NormalizeColor(value); if (Model.TextColor == c) return; Model.TextColor = c; OnPropertyChanged(); Changed(); }
    }

    public bool Hidden
    {
        get => Model.Hidden;
        set { if (Model.Hidden == value) return; Model.Hidden = value; OnPropertyChanged(); Changed(); }
    }

    public IReadOnlyList<ToolGroup> Groups => _owner.Groups;

    public string GroupId
    {
        get => Model.GroupId;
        set { if (value is not null && value != Model.GroupId) _owner.MoveToGroup(Model, value); }
    }

    /// <summary>The separator exactly as the hub will draw it.</summary>
    public HubToolVM Preview => new(Model, _config.Current.Hub, false);

    private void Changed()
    {
        OnPropertyChanged(nameof(Preview));
        _config.Touch(ConfigArea.Tools);
    }
}
