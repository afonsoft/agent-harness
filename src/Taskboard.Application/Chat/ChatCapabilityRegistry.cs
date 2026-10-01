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
        "shell_exec", "write_file", "run_cli", "code_interpreter", "run_agent", "memory",
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
            // Only the canonical ~/.agents/skills dir feeds the chat catalog —
            // per-CLI installs (~/.claude, ~/.cursor, …) stay on the Skills
            // management page. `skill:{name}` is a single toggle key, so the
            // catalog emits one row per id with the origins merged.
            var discovered = (await skills.DiscoverAsync(cancellationToken).ConfigureAwait(false))
                .Where(s => string.Equals(s.Source, SkillDiscoverySource.Agents, StringComparison.OrdinalIgnoreCase));
            foreach (var group in discovered
                .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var first = group.First();
                var id = $"skill:{first.Name}";
                var origins = group
                    .Select(s => s.Source)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(s => s, StringComparer.Ordinal)
                    .ToList();
                list.Add(new ChatCapability(
                    id,
                    ChatCapabilityKind.Skill,
                    first.Name,
                    first.Description,
                    IsCapabilityEnabled(id),
                    RequiresConfirmation: false,
                    Origin: origins.Count > 0 ? string.Join(", ", origins) : null));
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
