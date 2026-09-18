using Taskboard.Agents;

namespace Taskboard.Mcp;

/// <summary>Config-file syntax used by an agent CLI.</summary>
public enum McpConfigFormat
{
    Json,
    Toml
}

/// <summary>Shape of the managed MCP server entry for an agent CLI.</summary>
public enum McpEntryStyle
{
    /// <summary><c>{ url, transport: "http", headers }</c></summary>
    Devin,

    /// <summary><c>{ type: "http", url, headers }</c></summary>
    Claude,

    /// <summary><c>{ type: "remote", url, headers, enabled: true }</c></summary>
    OpenCode,

    /// <summary><c>{ url, headers }</c> — FastMCP <c>RemoteMCPServer</c>.</summary>
    OpenHands,

    /// <summary>TOML <c>[mcp_servers.&lt;n&gt;]</c> + <c>[mcp_servers.&lt;n&gt;.headers]</c>.</summary>
    Codex,

    /// <summary><c>{ serverUrl, headers }</c> — provisioned via <c>agy mcp add/remove</c>.</summary>
    Antigravity,

    /// <summary><c>{ url, headers }</c> — Kimi Code <c>mcp.json</c>.</summary>
    Kimi,

    /// <summary><c>{ httpUrl, headers }</c> — Qwen Code <c>settings.json</c>.</summary>
    Qwen,

    /// <summary><c>{ type: "http", url, headers }</c> — GitHub Copilot CLI <c>mcp-config.json</c>.</summary>
    Copilot,

    /// <summary><c>mcpServers.&lt;n&gt;.transport = { type, url }</c> — provisioned via <c>cline mcp add/remove</c>.</summary>
    Cline,

    /// <summary>JSON file dropped into <c>~/.continue/mcpServers/</c> (picked up automatically).</summary>
    Continue,

    /// <summary><c>{ url, headers }</c> — Kiro CLI <c>settings/mcp.json</c>.</summary>
    Kiro
}

/// <summary>Where and how an agent CLI stores its MCP server list.</summary>
public sealed record AgentMcpConfigTarget(
    string RelativePath,
    McpConfigFormat Format,
    string ContainerKey,
    McpEntryStyle Style);

/// <summary>
/// Maps each known agent CLI to its user-scope MCP configuration file and the
/// entry shape it understands (SPEC-20260917-rag-mcp-provisioning RF-002).
/// </summary>
public static class AgentMcpConfigMap
{
    private static readonly IReadOnlyDictionary<AgentType, AgentMcpConfigTarget> Targets =
        new Dictionary<AgentType, AgentMcpConfigTarget>
        {
            [AgentType.Devin] = new(
                ".config/devin/mcp_config.json", McpConfigFormat.Json, "mcpServers", McpEntryStyle.Devin),
            [AgentType.Claude] = new(
                ".claude.json", McpConfigFormat.Json, "mcpServers", McpEntryStyle.Claude),
            [AgentType.Codex] = new(
                ".codex/config.toml", McpConfigFormat.Toml, "mcp_servers", McpEntryStyle.Codex),
            [AgentType.OpenCode] = new(
                ".config/opencode/opencode.json", McpConfigFormat.Json, "mcp", McpEntryStyle.OpenCode),
            [AgentType.OpenHands] = new(
                ".openhands/mcp.json", McpConfigFormat.Json, "mcpServers", McpEntryStyle.OpenHands),
            [AgentType.Antigravity] = new(
                ".gemini/config/mcp_config.json", McpConfigFormat.Json, "mcpServers", McpEntryStyle.Antigravity),
            [AgentType.Kimi] = new(
                ".kimi-code/mcp.json", McpConfigFormat.Json, "mcpServers", McpEntryStyle.Kimi),
            [AgentType.Grok] = new(
                ".grok/config.toml", McpConfigFormat.Toml, "mcp_servers", McpEntryStyle.Codex),
            [AgentType.Qwen] = new(
                ".qwen/settings.json", McpConfigFormat.Json, "mcpServers", McpEntryStyle.Qwen),
            [AgentType.Copilot] = new(
                ".copilot/mcp-config.json", McpConfigFormat.Json, "mcpServers", McpEntryStyle.Copilot),
            [AgentType.Cline] = new(
                ".cline/data/settings/cline_mcp_settings.json", McpConfigFormat.Json, "mcpServers", McpEntryStyle.Cline),
            [AgentType.Continue] = new(
                ".continue/mcpServers", McpConfigFormat.Json, "mcpServers", McpEntryStyle.Continue),
            [AgentType.Kiro] = new(
                ".kiro/settings/mcp.json", McpConfigFormat.Json, "mcpServers", McpEntryStyle.Kiro),
        };

    /// <summary>
    /// Returns the target for <paramref name="agentType"/>, or <c>null</c> for
    /// unknown/future enum members (skipped with a warning by the caller).
    /// </summary>
    public static AgentMcpConfigTarget? GetTarget(AgentType agentType) =>
        Targets.TryGetValue(agentType, out var target) ? target : null;

    /// <summary>Absolute config path for <paramref name="target"/>.</summary>
    public static string GetConfigPath(AgentMcpConfigTarget target, string homeDirectory) =>
        Path.Join(homeDirectory, target.RelativePath);
}
