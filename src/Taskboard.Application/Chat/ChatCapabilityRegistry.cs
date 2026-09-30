using Microsoft.Extensions.Configuration;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Mcp;
using Taskboard.Application.Contracts.Skills;

namespace Taskboard.Application.Chat;

/// <summary>
/// Default <see cref="IChatCapabilityRegistry"/> (SPEC-20261001-chat-capability-
/// registry FR-002/003): enumerates tools, skills and delegation descriptors and
/// applies the enablement rules — per-kind master switches plus the
/// <c>Taskboard:Chat:Capabilities:Disabled</c> JSON id list.
/// </summary>
public sealed class ChatCapabilityRegistry(
    IReadOnlyDictionary<string, IChatTool> tools,
    ISkillDiscoveryService skills,
    IConfiguration configuration,
    IMcpClientManager? mcp = null) : IChatCapabilityRegistry
{
    /// <summary>Tools that mutate or execute — flagged for the Settings hint.</summary>
    private static readonly ISet<string> MutatingTools = new HashSet<string>(StringComparer.Ordinal)
    {
        "shell_exec", "write_file", "run_cli", "code_interpreter", "run_agent",
    };

    public async Task<IReadOnlyList<ChatCapability>> ListAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<ChatCapability>();
        var allTools = tools.Values.Concat(await McpToolsAsync(cancellationToken).ConfigureAwait(false));

        foreach (var tool in allTools.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            list.Add(new ChatCapability(
                tool.CapabilityId,
                tool.Kind,
                tool.Name,
                tool.Description,
                IsCapabilityEnabled(tool.CapabilityId),
                tool.RequiresConfirmation || MutatingTools.Contains(tool.Name),
                tool.Origin));
        }

        if (IsMasterOn(ChatCapabilityKind.Skill))
        {
            var discovered = await skills.DiscoverAsync(cancellationToken).ConfigureAwait(false);
            foreach (var skill in discovered.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            {
                var id = $"skill:{skill.Name}";
                list.Add(new ChatCapability(
                    id,
                    ChatCapabilityKind.Skill,
                    skill.Name,
                    skill.Description,
                    IsCapabilityEnabled(id),
                    RequiresConfirmation: false,
                    Origin: skill.Source));
            }
        }

        return list
            .OrderBy(c => c.Kind)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyDictionary<string, IChatTool>> ResolveToolSetAsync(CancellationToken cancellationToken = default)
    {
        var effective = tools
            .Where(kv => IsCapabilityEnabled(kv.Value.CapabilityId))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        // SPEC-20261001-chat-mcp-client: MCP adapters merge into the effective
        // set when the opt-in master is on; per-tool Disabled still applies.
        foreach (var adapter in await McpToolsAsync(cancellationToken).ConfigureAwait(false))
        {
            if (IsCapabilityEnabled(adapter.CapabilityId))
            {
                effective[adapter.Name] = adapter;
            }
        }

        return effective;
    }

    /// <summary>MCP adapters — [] when the bridge is absent or master off.</summary>
    private async Task<IReadOnlyList<IChatTool>> McpToolsAsync(CancellationToken cancellationToken)
    {
        if (mcp is null || !ChatCapabilityRules.IsMasterOn(configuration, ChatCapabilityKind.McpTool))
        {
            return [];
        }

        try
        {
            return await mcp.GetToolsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return [];
        }
    }

    public bool IsCapabilityEnabled(string capabilityId) =>
        ChatCapabilityRules.IsEnabled(configuration, capabilityId);

    private bool IsMasterOn(ChatCapabilityKind kind) =>
        ChatCapabilityRules.IsMasterOn(configuration, kind);
}
