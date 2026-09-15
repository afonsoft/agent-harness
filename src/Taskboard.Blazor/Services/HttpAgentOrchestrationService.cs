using System.Net.Http.Json;
using Taskboard.Agents;

namespace Taskboard.Blazor.Services;

/// <summary>
/// <see cref="IAgentOrchestrationService"/> implementation backed by the
/// server-side <c>/api/agents/*</c> endpoints
/// (SPEC-20260915-blazor-wasm-migration). Realtime log streaming still flows
/// through the <c>/agent-log-hub</c> SignalR hub.
/// </summary>
public sealed class HttpAgentOrchestrationService(HttpClient http) : IAgentOrchestrationService
{
    public async Task<IReadOnlyList<AgentInfo>> GetAvailableAgentsAsync(CancellationToken cancellationToken = default)
    {
        var result = await http.GetFromJsonAsync<AgentsResponse>("/api/agents", cancellationToken);
        return result?.Agents ?? [];
    }

    public async Task EnqueueAsync(AgentExecutionRequest request, CancellationToken cancellationToken = default)
    {
        var response = await http.PostAsJsonAsync("/api/agents/executions", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<AgentLogMessage>> GetLogsAsync(string issueId, CancellationToken cancellationToken = default)
    {
        var result = await http.GetFromJsonAsync<LogsResponse>(
            $"/api/agents/logs/{Uri.EscapeDataString(issueId)}", cancellationToken);
        return result?.Logs ?? [];
    }

    public async Task CancelAsync(string issueId, CancellationToken cancellationToken = default)
    {
        var response = await http.PostAsync(
            $"/api/agents/executions/{Uri.EscapeDataString(issueId)}/cancel", null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private sealed record AgentsResponse(List<AgentInfo> Agents);
    private sealed record LogsResponse(List<AgentLogMessage> Logs);
}
