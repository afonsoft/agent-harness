using System.Text.Json;
using System.Text.Json.Serialization;

namespace Taskboard.Integrations.Skills;

/// <summary>
/// Per-destination manifest (<c>.harness-skills.json</c>; legacy <c>.taskboard-skills.json</c> read as fallback) recording which
/// skills were installed by the sync engine and their content hash, so later
/// runs can copy only what changed and flag skills removed upstream.
/// </summary>
internal sealed record SkillsManifestEntry(
    [property: JsonPropertyName("hash")] string Hash,
    [property: JsonPropertyName("syncedAtUtc")] DateTimeOffset SyncedAtUtc,
    [property: JsonPropertyName("sourceRepo")] string SourceRepo,
    [property: JsonPropertyName("removedFromSource")] bool RemovedFromSource);

internal sealed class SkillsManifest
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("skills")]
    public Dictionary<string, SkillsManifestEntry> Skills { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public static SkillsManifest Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var manifest = JsonSerializer.Deserialize<SkillsManifest>(File.ReadAllText(path));
                if (manifest is not null)
                {
                    manifest.Skills = manifest.Skills is null
                        ? new Dictionary<string, SkillsManifestEntry>(StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, SkillsManifestEntry>(
                            manifest.Skills, StringComparer.OrdinalIgnoreCase);
                    return manifest;
                }
            }
        }
        catch
        {
            // Corrupt or unreadable manifest — treat as empty and rebuild.
        }

        return new SkillsManifest();
    }

    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
