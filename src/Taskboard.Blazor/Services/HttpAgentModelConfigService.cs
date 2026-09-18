using System.Net.Http.Json;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Blazor.Services;

/// <summary>
/// <see cref="IAgentModelConfigService"/> backed by
/// <c>/api/agents/{agentType}/models</c> (SPEC-20260918-agent-model-config).
/// </summary>
public sealed class HttpAgentModelConfigService(HttpClient http) : IAgentModelConfigService, IAgentModelCatalogService
{
    public async Task<AgentModelConfigDto> GetConfigAsync(AgentType agentType, CancellationToken cancellationToken = default)
    {
        var config = await http.GetFromJsonAsync<AgentModelConfigDto>(
            $"/api/agents/{agentType}/models", cancellationToken);
        return config ?? throw new InvalidOperationException("Empty model config response.");
    }

    public async Task SetConfigAsync(
        AgentType agentType, SaveAgentModelConfigRequest request, CancellationToken cancellationToken = default)
    {
        var response = await http.PutAsJsonAsync(
            $"/api/agents/{agentType}/models", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteConfigAsync(AgentType agentType, CancellationToken cancellationToken = default)
    {
        var response = await http.DeleteAsync($"/api/agents/{agentType}/models", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string?> ResolveModelAsync(
        AgentType agentType, AgentModelTier tier, CancellationToken cancellationToken = default)
    {
        var config = await GetConfigAsync(agentType, cancellationToken);
        return new AgentModelTierSet(config.Lite, config.Normal, config.Ultra).For(tier);
    }

    public async Task<IReadOnlyList<string>> ListAvailableAsync(
        AgentType agentType, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var response = await http.GetFromJsonAsync<AvailableAgentModelsResponse>(
            $"/api/agents/{agentType}/models/available{(forceRefresh ? "?refresh=true" : string.Empty)}",
            cancellationToken);
        return response?.Models ?? [];
    }
}
