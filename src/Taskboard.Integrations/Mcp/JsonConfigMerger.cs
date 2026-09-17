using System.Text.Json;
using System.Text.Json.Nodes;

namespace Taskboard.Integrations.Mcp;

public enum MergeOutcome
{
    NoChange,
    Updated,
    Removed,
    Repaired
}

/// <summary>
/// Idempotent merge of the managed MCP server entry into a JSON config file
/// (<c>System.Text.Json.Nodes</c> — no new dependency). Only the managed
/// <paramref name="name"/> node is touched; corrupt files are backed up to
/// <c>.corrupt-bak</c> and rebuilt (SPEC-20260917-rag-mcp-provisioning RF-003).
/// </summary>
public static class JsonConfigMerger
{
    /// <summary>
    /// Upserts <paramref name="entry"/> under <c>containerKey.name</c>, or
    /// removes it when <paramref name="entry"/> is null.
    /// </summary>
    public static MergeOutcome Merge(string path, string containerKey, string name, JsonObject? entry)
    {
        var (root, repaired) = ReadRoot(path);

        if (root[containerKey] is not JsonObject container)
        {
            container = new JsonObject();
            root[containerKey] = container;
        }

        var changed = false;
        if (entry is null)
        {
            changed = container.Remove(name);
        }
        else
        {
            var existing = container[name];
            if (existing is null
                || !JsonNode.DeepEquals(existing, entry))
            {
                container[name] = entry;
                changed = true;
            }
        }

        if (!changed && !repaired)
        {
            return MergeOutcome.NoChange;
        }

        SecureConfigWriter.WriteAtomic(
            path,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");

        if (repaired)
        {
            return MergeOutcome.Repaired;
        }

        return entry is null ? MergeOutcome.Removed : MergeOutcome.Updated;
    }

    /// <summary>
    /// Returns the URL of the managed entry, or null when absent/missing file.
    /// Throws when the file exists but is not a JSON object.
    /// </summary>
    public static string? ReadManagedUrl(string path, string containerKey, string name)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidDataException("config file is not a JSON object");
        return node[containerKey] is JsonObject container
            && container[name] is JsonObject entry
                ? entry["url"]?.GetValue<string>()
                : null;
    }

    private static (JsonObject Root, bool Repaired) ReadRoot(string path)
    {
        if (!File.Exists(path))
        {
            return (new JsonObject(), false);
        }

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (node is not null)
            {
                return (node, false);
            }
        }
        catch (JsonException)
        {
        }

        File.Copy(path, path + ".corrupt-bak", overwrite: true);
        return (new JsonObject(), true);
    }
}
