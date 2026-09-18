using System.Net.Http.Json;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Requests;

namespace Taskboard.Blazor.Services;

/// <summary>
/// <see cref="IAgentPromptTemplateService"/> implementation backed by the
/// server-side <c>/api/agents/prompt-template</c> endpoints.
/// </summary>
public sealed class HttpAgentPromptTemplateService(HttpClient http) : IAgentPromptTemplateService
{
    public async Task<AgentPromptTemplateInfo> GetTemplateAsync(CancellationToken cancellationToken = default)
    {
        var result = await http.GetFromJsonAsync<TemplateResponse>(
            "/api/agents/prompt-template", cancellationToken);
        return result is null
            ? new AgentPromptTemplateInfo(AgentPromptTemplate.Builtin, AgentPromptTemplate.Builtin, false)
            : new AgentPromptTemplateInfo(result.Template, result.Builtin, result.Customized);
    }

    public async Task SetTemplateAsync(string? template, CancellationToken cancellationToken = default)
    {
        var response = await http.PutAsJsonAsync(
            "/api/agents/prompt-template", new PromptTemplateRequest(template), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private sealed record TemplateResponse(string Template, string Builtin, bool Customized);
}
