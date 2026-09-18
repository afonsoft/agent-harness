using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// Per-CLI model tier configuration (SPEC-20260918-agent-model-config):
/// saved overrides win over the curated <see cref="AgentCliModels"/> table.
/// </summary>
public interface IAgentModelConfigService
{
    /// <summary>Effective configuration (override ?? curated) plus defaults and catalog.</summary>
    Task<AgentModelConfigDto> GetConfigAsync(AgentType agentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a per-CLI override. Null slots keep the curated default.
    /// Throws <see cref="ArgumentException"/> on invalid model names.
    /// </summary>
    Task SetConfigAsync(AgentType agentType, SaveAgentModelConfigRequest request, CancellationToken cancellationToken = default);

    /// <summary>Removes the saved override; the CLI falls back to the curated table.</summary>
    Task DeleteConfigAsync(AgentType agentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Effective model name for (type, tier): override ?? curated ?? null
    /// (CLI-managed). This is the single resolution point used by orchestration
    /// so argv and the persisted run record never diverge.
    /// </summary>
    Task<string?> ResolveModelAsync(AgentType agentType, AgentModelTier tier, CancellationToken cancellationToken = default);
}
