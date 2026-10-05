using System.Text.Json.Nodes;

using Taskboard.Application.Contracts.Mcp;
using Taskboard.Mcp;

namespace Taskboard.Integrations.Mcp;

/// <summary>
/// Shared write core for agent-CLI MCP config files
/// (SPEC-20261010-mcp-skills-hub RF-003): builds the per-style entry shape,
/// merges idempotently via <see cref="JsonConfigMerger"/>/
/// <see cref="TomlConfigMerger"/>, and writes atomically through
/// <see cref="SecureConfigWriter"/> (temp+rename, <c>.bak</c>, <c>0600</c>).
/// Used by both the RAG provisioning flow and the arbitrary-spec
/// <see cref="AgentMcpProvisioningService"/>.
/// </summary>
public static class McpConfigFileWriter
{
    /// <summary>
    /// Applies <paramref name="spec"/> to <paramref name="target"/>'s file at
    /// <paramref name="path"/>, or removes the entry when
    /// <paramref name="spec"/> is null.
    /// </summary>
    public static MergeOutcome Apply(
        string path,
        AgentMcpConfigTarget target,
        string name,
        ChatMcpServerSpec? spec)
    {
        if (target.Format == McpConfigFormat.Json)
        {
            var entry = spec is null ? null : BuildJsonEntry(target.Style, spec);
            return JsonConfigMerger.Merge(path, target.ContainerKey, name, entry);
        }

        return TomlConfigMerger.Merge(path, name, spec);
    }

    /// <summary>
    /// Per-<see cref="McpEntryStyle"/> JSON entry shape for an arbitrary spec —
    /// generalizes the RAG-only <c>BuildJsonEntry</c>: URL specs get the
    /// style's http shape; command specs get a stdio shape
    /// (<c>command</c>/<c>args</c>/<c>env</c>, OpenCode's <c>"local"</c>
    /// convention uses a command array + <c>environment</c>).
    /// </summary>
    public static JsonObject BuildJsonEntry(McpEntryStyle style, ChatMcpServerSpec spec)
    {
        var entry = !string.IsNullOrWhiteSpace(spec.Command)
            ? BuildCommandEntry(style, spec)
            : BuildUrlEntry(style, spec);

        return entry;
    }

    private static JsonObject BuildUrlEntry(McpEntryStyle style, ChatMcpServerSpec spec)
    {
        var entry = new JsonObject();
        var url = spec.Url
            ?? throw new InvalidOperationException(
                $"MCP server '{spec.Name}' needs command or url.");
        switch (style)
        {
            case McpEntryStyle.Devin:
                entry["url"] = url;
                entry["transport"] = "http";
                break;
            case McpEntryStyle.Claude:
            case McpEntryStyle.Copilot:
                entry["type"] = "http";
                entry["url"] = url;
                break;
            case McpEntryStyle.OpenCode:
                entry["type"] = "remote";
                entry["url"] = url;
                entry["enabled"] = true;
                break;
            case McpEntryStyle.OpenHands:
            case McpEntryStyle.Kimi:
            case McpEntryStyle.Kiro:
                entry["url"] = url;
                break;
            case McpEntryStyle.Qwen:
                entry["httpUrl"] = url;
                break;
            default:
                throw new InvalidOperationException($"No JSON entry shape for style '{style}'.");
        }

        if (spec.Headers is { Count: > 0 })
        {
            entry["headers"] = ToJsonObject(spec.Headers);
        }

        return entry;
    }

    private static JsonObject BuildCommandEntry(McpEntryStyle style, ChatMcpServerSpec spec)
    {
        var entry = new JsonObject();
        if (style == McpEntryStyle.OpenCode)
        {
            // opencode.json "local" transport: command is an argv array,
            // env vars live under "environment".
            var argv = new JsonArray { JsonValue.Create(spec.Command) };
            if (spec.Args is not null)
            {
                foreach (var arg in spec.Args)
                {
                    argv.Add(arg);
                }
            }

            entry["type"] = "local";
            entry["command"] = argv;
            entry["enabled"] = true;
            if (spec.Env is { Count: > 0 })
            {
                entry["environment"] = ToJsonObject(spec.Env);
            }

            return entry;
        }

        if (style == McpEntryStyle.Claude || style == McpEntryStyle.Copilot)
        {
            entry["type"] = "stdio";
        }

        entry["command"] = spec.Command;
        if (spec.Args is { Count: > 0 })
        {
            var args = new JsonArray();
            foreach (var arg in spec.Args)
            {
                args.Add(arg);
            }

            entry["args"] = args;
        }

        if (spec.Env is { Count: > 0 })
        {
            entry["env"] = ToJsonObject(spec.Env);
        }

        if (spec.Headers is { Count: > 0 })
        {
            entry["headers"] = ToJsonObject(spec.Headers);
        }

        return entry;
    }

    private static JsonObject ToJsonObject(IReadOnlyDictionary<string, string> map)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in map)
        {
            obj[key] = value;
        }

        return obj;
    }
}
