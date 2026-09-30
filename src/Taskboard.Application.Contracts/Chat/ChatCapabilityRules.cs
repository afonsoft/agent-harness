using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// Shared capability-enablement rules (SPEC-20261001-chat-capability-registry
/// FR-002): per-kind master switch AND not in the
/// <c>Taskboard:Chat:Capabilities:Disabled</c> JSON id list. Used by the
/// registry and by meta-tools (e.g. <c>use_skill</c>) that gate per-item ids.
/// </summary>
public static class ChatCapabilityRules
{
    public static bool IsEnabled(IConfiguration configuration, string capabilityId)
    {
        var kind = capabilityId switch
        {
            _ when capabilityId.StartsWith("mcp:", StringComparison.Ordinal) => ChatCapabilityKind.McpTool,
            _ when capabilityId.StartsWith("skill:", StringComparison.Ordinal) => ChatCapabilityKind.Skill,
            _ when capabilityId.StartsWith("agent:", StringComparison.Ordinal) => ChatCapabilityKind.AgentDelegation,
            _ => ChatCapabilityKind.BuiltinTool,
        };

        return IsMasterOn(configuration, kind) && !DisabledIds(configuration).Contains(capabilityId);
    }

    public static bool IsMasterOn(IConfiguration configuration, ChatCapabilityKind kind)
    {
        var raw = configuration[MasterKey(kind)];
        var master = string.IsNullOrWhiteSpace(raw) || (bool.TryParse(raw, out var on) && on);

        // MCP tools additionally require the opt-in MCP client master
        // (SPEC-20261001-chat-mcp-client FR-001, default off).
        if (kind == ChatCapabilityKind.McpTool)
        {
            var mcpRaw = configuration["Taskboard:Chat:Mcp:Enabled"];
            master = master && !string.IsNullOrWhiteSpace(mcpRaw) && bool.TryParse(mcpRaw, out var mcp) && mcp;
        }

        return master;
    }

    public static string MasterKey(ChatCapabilityKind kind) => kind switch
    {
        ChatCapabilityKind.Skill => "Taskboard:Chat:Skills:Enabled",
        ChatCapabilityKind.AgentDelegation => "Taskboard:Chat:AgentDelegation:Enabled",
        _ => "Taskboard:Chat:Tools:Enabled",
    };

    public static ISet<string> DisabledIds(IConfiguration configuration)
    {
        var raw = configuration["Taskboard:Chat:Capabilities:Disabled"];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        try
        {
            var ids = JsonSerializer.Deserialize<List<string>>(raw);
            return ids is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(ids, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }
}
