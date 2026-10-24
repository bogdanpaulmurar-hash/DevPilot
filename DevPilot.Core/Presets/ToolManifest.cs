using System.Text.Json;
using System.Text.Json.Serialization;
using DevPilot.Core.Config;
using DevPilot.Models;

namespace DevPilot.Core.Presets;

/// <summary>
/// Optional <c>devpilot.tool.json</c> placed next to a tool: lets the tool describe its own options
/// (its tray menu) so DevPilot can show them without a built-in preset.
/// </summary>
public sealed class ToolManifest
{
    public const string FileName = "devpilot.tool.json";

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Glyph { get; set; } = string.Empty;
    /// <summary>Optional logo (.png / .ico / .exe…), relative to the manifest's folder or absolute.</summary>
    public string Icon { get; set; } = string.Empty;
    public string PythonModule { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string AutoStartArguments { get; set; } = string.Empty;
    public string NoTrayArguments { get; set; } = string.Empty;
    public string ProcessMatch { get; set; } = string.Empty;
    public string HealthUrl { get; set; } = string.Empty;
    public List<ToolAction> Actions { get; set; } = [];

    /// <summary>Where the manifest was read from (not part of the file).</summary>
    [JsonIgnore] public string SourcePath { get; set; } = string.Empty;

    /// <summary>The "icon" as an absolute path, or empty.</summary>
    [JsonIgnore] public string IconFullPath => SourcePath.Length == 0 ? string.Empty : Hub.LogoSource.ManifestIconPath(SourcePath, Icon);

    /// <summary>Looks for the manifest in the tool's folder (and its parent, for src layouts).</summary>
    public static string? Find(ToolEntry tool)
    {
        var dir = Launch.PathTokens.ToolDirectory(tool);
        for (int i = 0; i < 2 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, FileName);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir) ?? string.Empty;
        }
        return null;
    }

    public static ToolManifest Load(string path)
    {
        var manifest = JsonSerializer.Deserialize<ToolManifest>(File.ReadAllText(path), ConfigSerializer.Options) ?? new ToolManifest();
        manifest.SourcePath = path;
        return manifest;
    }

    /// <summary>The logo declared by the tool's manifest, if it has one (never throws; empty when there is none).</summary>
    public static string IconFor(ToolEntry tool)
    {
        try { return Find(tool) is { } path ? Load(path).IconFullPath : string.Empty; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return string.Empty; }
    }

    /// <summary>Copies the manifest into the tool. Actions are replaced; other fields only fill empty values unless <paramref name="overwrite"/>.</summary>
    public void ApplyTo(ToolEntry tool, bool overwrite = false)
    {
        bool Fill(string current, string value) => !string.IsNullOrWhiteSpace(value) && (overwrite || string.IsNullOrWhiteSpace(current));
        if (Fill(tool.Name, Name)) tool.Name = Name;
        if (Fill(tool.Description, Description)) tool.Description = Description;
        if (Fill(tool.Glyph, Glyph)) tool.Glyph = Glyphs.Parse(Glyph);
        if (Fill(tool.IconPath, IconFullPath)) tool.IconPath = IconFullPath;
        if (Fill(tool.PythonModule, PythonModule)) tool.PythonModule = PythonModule;
        if (Fill(tool.Arguments, Arguments)) tool.Arguments = Arguments;
        if (Fill(tool.AutoStart.Arguments, AutoStartArguments)) tool.AutoStart.Arguments = AutoStartArguments;
        if (Fill(tool.Managed.NoTrayArguments, NoTrayArguments)) tool.Managed.NoTrayArguments = NoTrayArguments;
        if (Fill(tool.Monitor.ProcessMatch, ProcessMatch)) tool.Monitor.ProcessMatch = ProcessMatch;
        if (Fill(tool.Monitor.HealthUrl, HealthUrl)) tool.Monitor.HealthUrl = HealthUrl;
        if (Actions.Count > 0)
        {
            foreach (var a in Actions)
            {
                if (string.IsNullOrWhiteSpace(a.Id)) a.Id = NewId.Short();
                a.Glyph = Glyphs.Parse(a.Glyph);
            }
            tool.Actions = Actions;
        }
    }
}
