using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// Per-tier model names for one agent CLI (SPEC-20260918-agent-model-config).
/// A null slot falls back to the curated <see cref="AgentCliModels"/> default
/// for that tier.
/// </summary>
public sealed record AgentModelTierSet(string? Lite, string? Normal, string? Ultra)
{
    /// <summary>Model for <paramref name="tier"/> or null when unset.</summary>
    public string? For(AgentModelTier tier) => tier switch
    {
        AgentModelTier.Lite => Lite,
        AgentModelTier.Ultra => Ultra,
        _ => Normal,
    };
}

/// <summary>
/// Effective model configuration for a CLI: the per-tier values in force
/// (override ?? curated), whether they came from a saved override, the curated
/// defaults and the known-model catalog for the picker.
/// </summary>
public sealed record AgentModelConfigDto(
    AgentType AgentType,
    bool SupportsModelSelection,
    string Source,
    string? Lite,
    string? Normal,
    string? Ultra,
    AgentModelTierSet Defaults,
    IReadOnlyList<string> Catalog);

/// <summary>Payload for saving a per-CLI model override (null = curated default).</summary>
public sealed record SaveAgentModelConfigRequest(string? Lite, string? Normal, string? Ultra);

/// <summary>Model ids reported by the installed CLI itself (empty when unavailable).</summary>
public sealed record AvailableAgentModelsResponse(IReadOnlyList<string> Models);
