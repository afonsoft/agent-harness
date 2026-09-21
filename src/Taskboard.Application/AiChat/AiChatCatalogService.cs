using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Dtos;

namespace Taskboard.Application.AiChat;

/// <summary>
/// Aggregates the AI Chat model catalog from the eligible agent CLIs
/// (SPEC-20260921-ai-chat-cli-backend RF-001): per agent, the union of the
/// CLI-reported list (<see cref="IAgentModelCatalogService"/>), the curated
/// <see cref="AgentCliModels"/> table and the saved per-tier overrides —
/// plus custom entries registered via POST /api/local/ai/catalog.
/// </summary>
public sealed class AiChatCatalogService(
    AiCatalogService custom,
    IAgentEligibilityService eligibility,
    IAgentModelCatalogService probes,
    IAgentModelConfigService modelConfig,
    ILogger<AiChatCatalogService> logger)
{
    public async Task<IReadOnlyList<AiChatModelDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var eligible = await eligibility.GetEligibleTypesAsync(cancellationToken).ConfigureAwait(false);
        var models = new List<AiChatModelDto>();

        foreach (var agentType in eligible.OrderBy(t => t.ToString(), StringComparer.Ordinal))
        {
            var names = new List<string>();
            try
            {
                names.AddRange(await probes.ListAvailableAsync(agentType, cancellationToken: cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                // Probe failure degrades to curated + overrides.
                logger.LogWarning(ex, "Model probe failed for agent '{AgentType}'; falling back to curated models.", agentType);
            }

            names.AddRange(AgentCliModels.Catalog(agentType));

            try
            {
                var config = await modelConfig.GetConfigAsync(agentType, cancellationToken).ConfigureAwait(false);
                if (config.Lite is not null) names.Add(config.Lite);
                if (config.Normal is not null) names.Add(config.Normal);
                if (config.Ultra is not null) names.Add(config.Ultra);
            }
            catch (Exception ex)
            {
                // Overrides unavailable — catalog + probe still apply.
                logger.LogWarning(ex, "Model overrides unavailable for agent '{AgentType}'.", agentType);
            }

            foreach (var name in names.Distinct(StringComparer.Ordinal))
            {
                models.Add(new AiChatModelDto(
                    Id: $"{agentType}:{name}",
                    Provider: agentType.ToString(),
                    Name: name,
                    ReasoningEffortSupported: false,
                    AgentType: agentType.ToString()));
            }
        }

        // Custom entries are validated at POST time, but eligibility is dynamic —
        // a model whose agent was disabled since must not be offered.
        foreach (var entry in custom.List())
        {
            if (Enum.TryParse<AgentType>(entry.AgentType, true, out var customType) && eligible.Contains(customType))
            {
                models.Add(entry);
            }
        }

        return models;
    }

    /// <summary>
    /// Registers a custom model bound to an agent CLI. Returns the error code
    /// when the agent type is invalid or not eligible; <c>null</c> on success.
    /// </summary>
    public async Task<string?> AddAsync(AiChatModelDto model, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model.AgentType)
            || !Enum.TryParse<AgentType>(model.AgentType, true, out var agentType))
        {
            return "INVALID_AGENT";
        }

        var eligible = await eligibility.GetEligibleTypesAsync(cancellationToken).ConfigureAwait(false);
        if (!eligible.Contains(agentType))
        {
            return "agent-not-eligible";
        }

        return custom.TryAdd(model) ? null : "MODEL_EXISTS";
    }
}
