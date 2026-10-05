using Tomlyn;
using Tomlyn.Model;

using Taskboard.Application.Contracts.Mcp;

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
        var spec = string.IsNullOrWhiteSpace(url)
            ? null
            : new ChatMcpServerSpec(
                name,
                Url: url,
                Headers: string.IsNullOrWhiteSpace(apiKey)
                    ? null
                    : new Dictionary<string, string> { ["Authorization"] = $"Bearer {apiKey}" });
        return Merge(path, name, spec);
    }

    /// <summary>
    /// Spec-based merge (SPEC-20261010-mcp-skills-hub RF-003): http specs write
    /// <c>url</c> + <c>[headers]</c>; stdio specs write <c>command</c>,
    /// <c>args</c>, <c>[env]</c>; null removes the table.
    /// </summary>
    public static MergeOutcome Merge(string path, string name, ChatMcpServerSpec? spec)
    {
        var (root, repaired) = ReadRoot(path);

        if (!root.TryGetValue(ContainerKey, out var serversObj) || serversObj is not TomlTable servers)
        {
            servers = new TomlTable();
            root[ContainerKey] = servers;
        }

        var changed = false;
        var created = false;
        var removing = spec is null;
        if (removing)
        {
            changed = servers.Remove(name);
        }
        else
        {
            var desired = BuildServerTable(spec!);
            if (!servers.TryGetValue(name, out var existingObj)
                || existingObj is not TomlTable existing
                || !TablesEqual(existing, desired))
            {
                created = existingObj is null;
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

        if (removing)
        {
            return MergeOutcome.Removed;
        }

        return created ? MergeOutcome.Created : MergeOutcome.Updated;
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
        if (!root.TryGetValue(ContainerKey, out var serversObj)
            || serversObj is not TomlTable servers
            || !servers.TryGetValue(name, out var entryObj)
            || entryObj is not TomlTable entry)
        {
            return null;
        }
        return entry.TryGetValue("url", out var url) ? url as string : null;
    }

    private static TomlTable BuildServerTable(ChatMcpServerSpec spec)
    {
        var table = new TomlTable();
        if (!string.IsNullOrWhiteSpace(spec.Command))
        {
            table["command"] = spec.Command;
            if (spec.Args is { Count: > 0 })
            {
                var args = new TomlArray();
                foreach (var arg in spec.Args)
                {
                    args.Add(arg);
                }

                table["args"] = args;
            }

            if (spec.Env is { Count: > 0 })
            {
                var env = new TomlTable();
                foreach (var (key, value) in spec.Env)
                {
                    env[key] = value;
                }

                table["env"] = env;
            }
        }
        else
        {
            table["url"] = spec.Url
                ?? throw new InvalidOperationException(
                    $"MCP server '{spec.Name}' needs command or url.");
        }

        if (spec.Headers is { Count: > 0 })
        {
            var headers = new TomlTable();
            foreach (var (key, value) in spec.Headers)
            {
                headers[key] = value;
            }

            table["headers"] = headers;
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
                (TomlArray aa, TomlArray ab) => aa.SequenceEqual(ab),
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
            // Malformed config — treated as absent.
        }

        File.Copy(path, path + ".corrupt-bak", overwrite: true);
        return (new TomlTable(), true);
    }
}
