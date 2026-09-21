using System.Net.Http.Json;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;

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

    public async Task<bool> EnqueueAsync(AgentExecutionRequest request, CancellationToken cancellationToken = default)
    {
        var response = await http.PostAsJsonAsync("/api/agents/executions", request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<IReadOnlyList<AgentLogMessage>> GetLogsAsync(string issueId, CancellationToken cancellationToken = default)
    {
        var result = await http.GetFromJsonAsync<LogsResponse>(
            $"/api/agents/logs/{Uri.EscapeDataString(issueId)}", cancellationToken);
        return result?.Logs ?? [];
    }

    public async Task ClearLogsAsync(string issueId, CancellationToken cancellationToken = default)
    {
        var response = await http.DeleteAsync(
            $"/api/agents/logs/{Uri.EscapeDataString(issueId)}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> CancelAsync(string issueId, CancellationToken cancellationToken = default)
    {
        // Unified control surface — 202 when a live run was signalled,
        // 409 when there is no active execution for the issue.
        var response = await http.PostAsJsonAsync("/api/agents/control",
            new AgentControlRequest("issue", issueId, "cancel"), cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<AgentRunDto>> GetRunsAsync(string issueId, int take = 5, CancellationToken cancellationToken = default)
    {
        var result = await http.GetFromJsonAsync<RunsResponse>(
            $"/api/agents/runs?issueId={Uri.EscapeDataString(issueId)}&take={take}", cancellationToken);
        return result?.Runs ?? [];
    }

    public async Task<IReadOnlyList<AgentRunDto>> GetLatestRunsAsync(CancellationToken cancellationToken = default)
    {
        var result = await http.GetFromJsonAsync<RunsResponse>("/api/agents/runs/active", cancellationToken);
        return result?.Runs ?? [];
    }

    /// <summary>Client proxy — live run tracking exists only server-side.</summary>
    public IReadOnlyCollection<Guid> GetLiveRunIds() => [];

    private sealed record AgentsResponse(List<AgentInfo> Agents);
    private sealed record LogsResponse(List<AgentLogMessage> Logs);
    private sealed record RunsResponse(List<AgentRunDto> Runs);
}
