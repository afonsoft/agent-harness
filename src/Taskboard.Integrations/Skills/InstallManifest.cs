using System.Text.Json;
using System.Text.Json.Serialization;

namespace Taskboard.Integrations.Skills;

/// <summary>
/// Persisted record of the last skills install (<c>skills-install.json</c> in
/// the data directory). Never stores env vars, tokens or process output.
/// </summary>
internal sealed class InstallManifest
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("repository")]
    public string? Repository { get; set; }

    [JsonPropertyName("installedAtUtc")]
    public DateTimeOffset? InstalledAtUtc { get; set; }

    [JsonPropertyName("durationMs")]
    public long? DurationMs { get; set; }

    [JsonPropertyName("skillCount")]
    public int SkillCount { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("npxStep")]
    public object? NpxStep { get; set; }

    [JsonPropertyName("installShStep")]
    public object? InstallShStep { get; set; }

    public static InstallManifest? Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<InstallManifest>(File.ReadAllText(path));
            }
        }
        catch
        {
            // Corrupt or unreadable manifest — treated as never installed.
        }

        return null;
    }

    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }
}
