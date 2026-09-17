using Tomlyn;
using Tomlyn.Model;

namespace Taskboard.Integrations.Mcp;

/// <summary>
/// Idempotent merge of the managed MCP server entry into a TOML config file
/// (Codex <c>~/.codex/config.toml</c>). Only the <c>mcp_servers.&lt;name&gt;</c>
/// table is touched; corrupt files are backed up to <c>.corrupt-bak</c> and
/// rebuilt (SPEC-20260917-rag-mcp-provisioning RF-003).
/// </summary>
public static class TomlConfigMerger
{
    private const string ContainerKey = "mcp_servers";

    /// <summary>
    /// Upserts <c>[mcp_servers.&lt;name&gt;]</c> with <paramref name="url"/> and
    /// an optional <c>headers.Authorization</c> Bearer entry; removes the table
    /// when <paramref name="url"/> is empty.
    /// </summary>
    public static MergeOutcome Merge(string path, string name, string? url, string? apiKey)
    {
        var (root, repaired) = ReadRoot(path);

        if (!root.TryGetValue(ContainerKey, out var serversObj) || serversObj is not TomlTable servers)
        {
            servers = new TomlTable();
            root[ContainerKey] = servers;
        }

        var changed = false;
        var removing = string.IsNullOrWhiteSpace(url);
        if (removing)
        {
            changed = servers.Remove(name);
        }
        else
        {
            var desired = BuildServerTable(url!, apiKey);
            if (!servers.TryGetValue(name, out var existingObj)
                || existingObj is not TomlTable existing
                || !TablesEqual(existing, desired))
            {
                servers[name] = desired;
                changed = true;
            }
        }

        if (!changed && !repaired)
        {
            return MergeOutcome.NoChange;
        }

        SecureConfigWriter.WriteAtomic(path, TomlSerializer.Serialize(root));

        if (repaired)
        {
            return MergeOutcome.Repaired;
        }

        return removing ? MergeOutcome.Removed : MergeOutcome.Updated;
    }

    /// <summary>
    /// Returns the URL of the managed entry, or null when absent/missing file.
    /// Throws when the file exists but is not valid TOML.
    /// </summary>
    public static string? ReadManagedUrl(string path, string name)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var root = TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(path)) ?? new TomlTable();
        return root.TryGetValue(ContainerKey, out var serversObj)
            && serversObj is TomlTable servers
            && servers.TryGetValue(name, out var entryObj)
            && entryObj is TomlTable entry
                ? entry.TryGetValue("url", out var url) ? url as string : null
                : null;
    }

    private static TomlTable BuildServerTable(string url, string? apiKey)
    {
        var table = new TomlTable { ["url"] = url };
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            table["headers"] = new TomlTable
            {
                ["Authorization"] = $"Bearer {apiKey}"
            };
        }

        return table;
    }

    private static bool TablesEqual(TomlTable a, TomlTable b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var other))
            {
                return false;
            }

            var equal = (value, other) switch
            {
                (TomlTable ta, TomlTable tb) => TablesEqual(ta, tb),
                _ => Equals(value, other)
            };
            if (!equal)
            {
                return false;
            }
        }

        return true;
    }

    private static (TomlTable Root, bool Repaired) ReadRoot(string path)
    {
        if (!File.Exists(path))
        {
            return (new TomlTable(), false);
        }

        try
        {
            return (TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(path)) ?? new TomlTable(), false);
        }
        catch (TomlException)
        {
        }

        File.Copy(path, path + ".corrupt-bak", overwrite: true);
        return (new TomlTable(), true);
    }
}
