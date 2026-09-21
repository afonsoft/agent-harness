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
    IAgentModelConfigService modelConfig)
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
            catch
            {
                // Probe failure degrades to curated + overrides.
            }

            names.AddRange(AgentCliModels.Catalog(agentType));

            try
            {
                var config = await modelConfig.GetConfigAsync(agentType, cancellationToken).ConfigureAwait(false);
                if (config.Lite is not null) names.Add(config.Lite);
                if (config.Normal is not null) names.Add(config.Normal);
                if (config.Ultra is not null) names.Add(config.Ultra);
            }
            catch
            {
                // Overrides unavailable — catalog + probe still apply.
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

        models.AddRange(custom.List());
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
