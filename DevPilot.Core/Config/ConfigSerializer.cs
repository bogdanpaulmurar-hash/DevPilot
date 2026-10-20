using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DevPilot.Models;

namespace DevPilot.Core.Config;

/// <summary>JSON (de)serialisation of the v2 configuration, with v1 detection.</summary>
public static class ConfigSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings, Options);

    /// <summary>
    /// Returns the schema of a config document: 2 for v2, 1 for the legacy layout, 0 if unknown.
    /// </summary>
    public static int DetectSchema(string json)
    {
        var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) as JsonObject;
        if (node is null) return 0;
        foreach (var (key, value) in node)
        {
            if (string.Equals(key, "schemaVersion", StringComparison.OrdinalIgnoreCase) && value is JsonValue v
                && v.TryGetValue<int>(out var n))
                return n;
        }
        string[] v1Keys = ["apps", "custom_tools", "tray_type_configs", "jiggler_enabled", "heartbeat_targets", "minimize_to_tray"];
        return node.Any(kv => v1Keys.Contains(kv.Key)) ? 1 : 0;
    }

    /// <summary>Parses any supported config document into a normalised v2 object.</summary>
    public static AppSettings Load(string json, out bool migrated)
    {
        migrated = false;
        var schema = DetectSchema(json);
        AppSettings result;
        if (schema == 1)
        {
            var legacy = JsonSerializer.Deserialize<LegacyV1Settings>(json, LegacyV1Settings.Options)
                         ?? new LegacyV1Settings();
            result = ConfigMigrator.FromV1(legacy);
            migrated = true;
        }
        else
        {
            result = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? Defaults.Create();
            if (result.SchemaVersion < AppSettings.CurrentSchema) migrated = true;
        }
        Normalizer.Normalize(result);
        return result;
    }
}
