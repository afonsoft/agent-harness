using System.Net.Http.Json;
using System.Text.Json;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Configuration;
using Taskboard.Application.Contracts.Mcp;
using Taskboard.Application.Contracts.Operations;
using Taskboard.Application.Contracts.Settings;
using Taskboard.Application.Contracts.Skills;
using Taskboard.Dtos;
using Taskboard.Requests;

namespace Taskboard.Blazor.Services;

/// <summary>
/// Cliente HTTP para consumir a API REST do Taskboard a partir do frontend Blazor.
/// </summary>
public sealed class TaskboardClient
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Cria uma nova instância de <see cref="TaskboardClient"/>.
    /// </summary>
    public TaskboardClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Retorna os workspaces de workflow registrados.
    /// </summary>
    public async Task<IReadOnlyList<WorkflowWorkspaceDto>> GetWorkflowWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<WorkflowWorkspaceListResponse>("/api/device-workspaces", cancellationToken);
        return response?.Workspaces ?? [];
    }

    /// <summary>
    /// Retorna todas as threads de chat de IA.
    /// </summary>
    public async Task<IReadOnlyCollection<AiChatThreadDto>> GetAiChatThreadsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<AiChatThreadListResponse>("/api/local/ai/threads", cancellationToken);
        return response?.Threads ?? [];
    }

    public async Task<SettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<SettingsResponse>("/api/settings", cancellationToken);
        return response?.Settings ?? new SettingsDto("light", null, []);
    }

    public async Task SaveSettingsAsync(SaveSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync("/api/settings", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<SkillDto>> GetSkillsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<SkillsResponse>("/api/skills", cancellationToken);
        return response?.Skills ?? [];
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync("/api/admin/password", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<SkillDetailDto?> GetSkillDetailAsync(string source, string name, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/skills/{Uri.EscapeDataString(source)}/{Uri.EscapeDataString(name)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SkillDetailResponse>(cancellationToken);
        return result?.Skill;
    }

    /// <summary>
    /// Retorna o conteúdo de texto de um arquivo dentro do diretório da skill.
    /// </summary>
    public async Task<string?> GetSkillFileContentAsync(string source, string name, string relativePath, CancellationToken cancellationToken = default)
    {
        var encodedPath = string.Join('/', relativePath.Split('/').Select(Uri.EscapeDataString));
        var response = await _httpClient.GetAsync(
            $"/api/skills/{Uri.EscapeDataString(source)}/{Uri.EscapeDataString(name)}/files/{encodedPath}",
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var result = await response.Content.ReadFromJsonAsync<SkillFileContentResponse>(cancellationToken);
        return result?.Content;
    }

    /// <summary>
    /// Retorna o catálogo de configuração com valor efetivo e fonte de cada chave.
    /// </summary>
    public async Task<IReadOnlyList<ConfigurationEntryDto>> GetConfigurationEntriesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<ConfigurationEntriesResponse>("/api/configuration", cancellationToken);
        return response?.Entries ?? [];
    }

    /// <summary>
    /// Persiste um override de configuração. Retorna a mensagem de erro da API ou <c>null</c> em sucesso.
    /// </summary>
    public async Task<string?> SetConfigurationValueAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync(
            $"/api/configuration/{Uri.EscapeDataString(key)}",
            new SetConfigurationRequest(value),
            cancellationToken);
        return response.IsSuccessStatusCode ? null : await ReadErrorMessageAsync(response, cancellationToken);
    }

    /// <summary>
    /// Remove o override de configuração. Retorna a mensagem de erro da API ou <c>null</c> em sucesso.
    /// </summary>
    public async Task<string?> DeleteConfigurationValueAsync(string key, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync(
            $"/api/configuration/{Uri.EscapeDataString(key)}",
            cancellationToken);
        return response.IsSuccessStatusCode ? null : await ReadErrorMessageAsync(response, cancellationToken);
    }

    /// <summary>Status do instalador global de skills (SPEC-20260917-skills-installer).</summary>
    public async Task<SkillsInstallStatus?> GetSkillsInstallStatusAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<SkillsInstallStatus>("/api/skills/install/status", cancellationToken);

    /// <summary>Dispara a instalação global em background (202 Accepted).</summary>
    public async Task<SkillsInstallStatus?> InstallSkillsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync("/api/skills/install", content: null, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<SkillsInstallStatus>(cancellationToken)
            : null;
    }

    /// <summary>Re-verifica os diretórios globais de skills.</summary>
    public async Task<SkillsInstallStatus?> VerifySkillsInstallAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync("/api/skills/install/verify", content: null, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<SkillsInstallStatus>(cancellationToken)
            : null;
    }

    /// <summary>Status da sincronização de skills por agente.</summary>
    public async Task<SkillsSyncStatus?> GetSkillsSyncStatusAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<SkillsSyncStatus>("/api/skills/sync/status", cancellationToken);

    /// <summary>Dispara a sincronização de skills (202 Accepted).</summary>
    public async Task<SkillsSyncStatus?> SyncSkillsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync("/api/skills/sync", content: null, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<SkillsSyncStatus>(cancellationToken)
            : null;
    }

    /// <summary>Log de processo das últimas instalações/sincronizações de skills.</summary>
    public async Task<IReadOnlyList<OperationLogEntry>> GetSkillsLogAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<IReadOnlyList<OperationLogEntry>>("/api/skills/log", cancellationToken)
        ?? [];

    /// <summary>Status do provisionamento MCP por agente (SPEC-20260917-rag-mcp-provisioning).</summary>
    public async Task<McpProvisionStatus?> GetMcpStatusAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<McpProvisionStatus>("/api/mcp/status", cancellationToken);

    /// <summary>
    /// Persiste a configuração RAG e agenda o provisionamento.
    /// <paramref name="apiKey"/> nulo mantém a chave gravada; vazio a remove.
    /// Retorna a mensagem de erro da API ou <c>null</c> em sucesso.
    /// </summary>
    public async Task<string?> SaveRagMcpAsync(
        string? name, string? url, string? apiKey, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync(
            "/api/mcp/rag",
            new SaveRagMcpRequest(name, url, apiKey),
            cancellationToken);
        return response.IsSuccessStatusCode ? null : await ReadErrorMessageAsync(response, cancellationToken);
    }

    /// <summary>
    /// Dispara o provisionamento MCP em background (202 Accepted).
    /// Retorna (status, erro) — erro quando a URL ainda não foi salva
    /// (SPEC-20260918-rag-mcp-sync: Sync nunca remove implicitamente).
    /// </summary>
    public async Task<(McpProvisionStatus? Status, string? Error)> SyncMcpAsync(
        CancellationToken cancellationToken = default) =>
        await PostMcpAsync("/api/mcp/sync", cancellationToken);

    /// <summary>Remove explicitamente a entrada gerenciada de todos os CLIs (202).</summary>
    public async Task<(McpProvisionStatus? Status, string? Error)> RemoveMcpAsync(
        CancellationToken cancellationToken = default) =>
        await PostMcpAsync("/api/mcp/remove", cancellationToken);

    private async Task<(McpProvisionStatus? Status, string? Error)> PostMcpAsync(
        string path, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsync(path, content: null, cancellationToken);
        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<McpProvisionStatus>(cancellationToken), null)
            : (null, await ReadErrorMessageAsync(response, cancellationToken));
    }

    /// <summary>Log de processo das últimas execuções de provisionamento MCP.</summary>
    public async Task<IReadOnlyList<OperationLogEntry>> GetMcpLogAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<IReadOnlyList<OperationLogEntry>>("/api/mcp/log", cancellationToken)
        ?? [];

    /// <summary>Status dos CLIs de agente (instalado/versão/auth) — SPEC-20260917-cli-agents-terminal.</summary>
    public async Task<IReadOnlyList<AgentCliStatus>> GetAgentClisAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<IReadOnlyList<AgentCliStatus>>("/api/agent-clis", cancellationToken)
        ?? [];

    /// <summary>Inicia a instalação gerenciada de um CLI (SPEC-20260918-cli-agents-expansion).</summary>
    public async Task<AgentCliInstallStatus?> StartAgentCliInstallAsync(
        Taskboard.Agents.AgentCliKind kind, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"/api/agent-clis/{kind.ToString().ToLowerInvariant()}/install", null, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<AgentCliInstallStatus>(cancellationToken)
            : null;
    }

    /// <summary>Snapshot do último run de instalação de um CLI.</summary>
    public async Task<AgentCliInstallStatus?> GetAgentCliInstallStatusAsync(
        Taskboard.Agents.AgentCliKind kind, CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<AgentCliInstallStatus>(
            $"/api/agent-clis/{kind.ToString().ToLowerInvariant()}/install/status", cancellationToken);

    /// <summary>Status do code-server (instalado/versão/rodando) — SPEC-20260917-vscode-web-workspace.</summary>
    public async Task<Taskboard.Application.Contracts.Vscode.VscodeStatus?> GetVscodeStatusAsync(
        CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<Taskboard.Application.Contracts.Vscode.VscodeStatus>(
            "/api/vscode/status", cancellationToken);

    /// <summary>Dispara a instalação gerenciada do code-server (202 Accepted).</summary>
    public async Task<Taskboard.Application.Contracts.Vscode.VscodeInstallStatus?> StartVscodeInstallAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync("/api/vscode/install", null, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<Taskboard.Application.Contracts.Vscode.VscodeInstallStatus>(cancellationToken)
            : null;
    }

    /// <summary>Snapshot do último run de instalação do code-server.</summary>
    public async Task<Taskboard.Application.Contracts.Vscode.VscodeInstallStatus?> GetVscodeInstallStatusAsync(
        CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<Taskboard.Application.Contracts.Vscode.VscodeInstallStatus>(
            "/api/vscode/install/status", cancellationToken);

    /// <summary>Workdir resolvido de um card (owner/name) sob o workspace root.</summary>
    public async Task<Taskboard.Application.Contracts.Vscode.VscodeWorkdir?> GetVscodeWorkdirAsync(
        string repositoryFullName, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"/api/vscode/workdir?repo={Uri.EscapeDataString(repositoryFullName)}", cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<Taskboard.Application.Contracts.Vscode.VscodeWorkdir>(cancellationToken)
            : null;
    }

    /// <summary>Timeline unificada da issue: eventos do board + execuções de agente, mais recente primeiro.</summary>
    public async Task<IReadOnlyList<Taskboard.GitHub.IssueHistoryItemDto>> GetIssueHistoryAsync(
        string issueId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<IssueHistoryResponse>(
            $"/api/github/issues/{Uri.EscapeDataString(issueId)}/history", cancellationToken);
        return (IReadOnlyList<Taskboard.GitHub.IssueHistoryItemDto>?)response?.Items ?? [];
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var message = document.RootElement
                .GetProperty("error")
                .GetProperty("message")
                .GetString();
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }
        }
        catch (JsonException)
        {
        }
        catch (KeyNotFoundException)
        {
        }

        return $"Request failed with status {(int)response.StatusCode}.";
    }

    private sealed record IssueHistoryResponse(List<Taskboard.GitHub.IssueHistoryItemDto> Items);

    private sealed record WorkflowWorkspaceListResponse(List<WorkflowWorkspaceDto> Workspaces);
    private sealed record AiChatThreadListResponse(List<AiChatThreadDto> Threads);
    private sealed record SettingsResponse(SettingsDto Settings);
    private sealed record SkillsResponse(List<SkillDto> Skills);
    private sealed record SkillDetailResponse(SkillDetailDto Skill);
    private sealed record ConfigurationEntriesResponse(List<ConfigurationEntryDto> Entries);
    private sealed record SkillFileContentResponse(string Path, string Content);
}
