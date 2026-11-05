using System.Reflection;
using Microsoft.Extensions.Logging;
using Tomlyn;
using Tomlyn.Model;

namespace Taskboard.Integrations.Agents.Attention;

/// <summary>
/// SPEC-20261104-agent-attention-manifests RF-004/RF-005: manifest discovery —
/// bundled catalog embedded as resources (mirroring
/// <c>distribution/agent-detection/</c>) shadowed by local override files under
/// the configured directory. A bad override never blocks the bundled manifest:
/// it is skipped with a warning that surfaces in <c>explain</c>.
/// </summary>
public sealed class AgentDetectionManifestLoader
{
    /// <summary>Embedded resource prefix for bundled manifests.</summary>
    internal const string ResourcePrefix = "Taskboard.Detection.";

    private const int SupportedSchemaVersion = 1;

    private readonly string? _overrideDir;
    private readonly ILogger? _logger;
    private readonly Func<IReadOnlyDictionary<string, string>>? _bundledSource;

    public AgentDetectionManifestLoader(string? overrideDir, ILogger? logger = null)
    {
        _overrideDir = string.IsNullOrWhiteSpace(overrideDir) ? null : overrideDir;
        _logger = logger;
    }

    /// <summary>Test seam — bundled set supplied in-memory instead of resources.</summary>
    internal AgentDetectionManifestLoader(
        IReadOnlyDictionary<string, string> bundled, string? overrideDir, ILogger? logger = null)
        : this(overrideDir, logger)
    {
        _bundledSource = () => bundled;
    }

    /// <summary>Loads the full manifest set; never throws — failures become warnings.</summary>
    public DetectionManifestSet Load()
    {
        var warnings = new List<string>();
        var byId = new Dictionary<string, LoadedDetectionManifest>(StringComparer.OrdinalIgnoreCase);
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Bundled first — overrides shadow afterwards.
        var bundled = (_bundledSource ?? LoadBundledResources)();
        var indexError = ValidateIndex(bundled);
        if (indexError is not null)
        {
            warnings.Add(indexError);
        }
        else
        {
            foreach (var (name, toml) in bundled)
            {
                if (name.Equals("index.toml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                TryAdd(byId, aliases, toml, $"bundled:{name}", DetectionSources.Bundled, warnings);
            }
        }

        var overrideCount = 0;
        if (_overrideDir is not null && Directory.Exists(_overrideDir))
        {
            foreach (var file in Directory.EnumerateFiles(_overrideDir, "*.toml"))
            {
                string toml;
                try
                {
                    toml = File.ReadAllText(file);
                }
                catch (Exception ex)
                {
                    warnings.Add($"override:{Path.GetFileName(file)} unreadable — {ex.Message}");
                    continue;
                }

                var before = byId.Count;
                if (TryAdd(byId, aliases, toml, $"override:{Path.GetFileName(file)}",
                        DetectionSources.Override, warnings, replacing: true))
                {
                    overrideCount++;
                }
                else if (byId.Count == before)
                {
                    warnings.Add(
                        $"override:{Path.GetFileName(file)} rejected — bundled manifest kept");
                }
            }
        }

        return new DetectionManifestSet(byId, aliases, warnings, overrideCount);
    }

    /// <summary>Catalog gate: <c>schema_version</c> above supported ignores the whole bundled set.</summary>
    private static string? ValidateIndex(IReadOnlyDictionary<string, string> bundled)
    {
        if (!bundled.TryGetValue("index.toml", out var indexToml))
        {
            return null; // no index — load whatever resources exist
        }

        try
        {
            var table = TomlSerializer.Deserialize<TomlTable>(indexToml);
            if (table?.TryGetValue("schema_version", out var v) == true && v is long schema
                && schema > SupportedSchemaVersion)
            {
                return $"bundled catalog schema_version {schema} > {SupportedSchemaVersion} — catalog ignored";
            }
            return null;
        }
        catch (Exception ex)
        {
            return $"bundled index.toml invalid — catalog ignored ({ex.Message})";
        }
    }

    private bool TryAdd(
        Dictionary<string, LoadedDetectionManifest> byId,
        Dictionary<string, string> aliases,
        string toml, string sourceName, string source,
        List<string> warnings, bool replacing = false)
    {
        AgentDetectionManifest manifest;
        try
        {
            manifest = AgentDetectionManifest.Parse(toml, sourceName);
        }
        catch (ManifestException ex)
        {
            warnings.Add(ex.Message);
            return false;
        }

        if (manifest.MinEngineVersion > AgentDetectionEngine.EngineVersion)
        {
            warnings.Add(
                $"{sourceName}: manifest '{manifest.Id}' requires engine " +
                $"{manifest.MinEngineVersion} > {AgentDetectionEngine.EngineVersion} — skipped (incompatible)");
            return false;
        }

        if (!replacing && byId.ContainsKey(manifest.Id))
        {
            warnings.Add($"{sourceName}: duplicate manifest id '{manifest.Id}' — first wins");
            return false;
        }

        byId[manifest.Id] = new LoadedDetectionManifest(manifest, source);
        aliases[manifest.Id] = manifest.Id;
        foreach (var alias in manifest.Aliases)
        {
            aliases[alias] = manifest.Id;
        }
        return true;
    }

    private static IReadOnlyDictionary<string, string> LoadBundledResources()
    {
        var assembly = typeof(AgentDetectionManifestLoader).Assembly;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                || !name.EndsWith(".toml", StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            result[name[ResourcePrefix.Length..]] = reader.ReadToEnd();
        }
        return result;
    }
}

/// <summary>Manifest provenance — surfaced in explain as <c>manifestSource</c>.</summary>
public static class DetectionSources
{
    public const string Bundled = "bundled";
    public const string Override = "override";
    public const string Remote = "remote";
    public const string None = "none";
}

public sealed record LoadedDetectionManifest(AgentDetectionManifest Manifest, string Source);

/// <summary>Immutable manifest set — swapped atomically on reload.</summary>
public sealed record DetectionManifestSet(
    IReadOnlyDictionary<string, LoadedDetectionManifest> ById,
    IReadOnlyDictionary<string, string> Aliases,
    IReadOnlyList<string> Warnings,
    int OverrideCount)
{
    public static readonly DetectionManifestSet Empty = new(
        new Dictionary<string, LoadedDetectionManifest>(),
        new Dictionary<string, string>(),
        [], 0);
}
