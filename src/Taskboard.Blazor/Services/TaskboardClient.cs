using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Configuration;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Contracts.Jobs;
using Taskboard.Application.Contracts.Mcp;
using Taskboard.Application.Contracts.Operations;
using Taskboard.Application.Contracts.Settings;
using Taskboard.Application.Contracts.Workspace;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Skills;
using Taskboard.Dtos;
using Taskboard.Harness.FinOps;
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
    /// Retorna todas as threads de chat de IA.
    /// </summary>
    public async Task<IReadOnlyCollection<AiChatThreadDto>> GetAiChatThreadsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<AiChatThreadListResponse>("/api/local/ai/threads", cancellationToken);
        return response?.Threads ?? [];
    }

    /// <summary>
    /// Catálogo de modelos disponíveis para novas threads. Com
    /// <paramref name="threadId"/>, modelos reportados pela sessão ACP ativa
    /// têm prioridade (SPEC-20260921-ai-code-thread-config RF-005).
    /// </summary>
    public async Task<IReadOnlyList<AiChatModelDto>> GetAiChatCatalogAsync(
        string? threadId = null, CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(threadId)
            ? "/api/local/ai/catalog"
            : $"/api/local/ai/catalog?threadId={Uri.EscapeDataString(threadId)}";
        var response = await _httpClient.GetFromJsonAsync<AiChatCatalogResponse>(url, cancellationToken);
        return response?.Models ?? [];
    }

    /// <summary>Cria uma thread de chat (201) — retorna (thread, erro) com o detail do problem+json.</summary>
    public async Task<(AiChatThreadDto? Thread, string? Error)> CreateAiChatThreadAsync(
        CreateAiChatThreadRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/local/ai/threads", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (null, await ReadErrorAsync(response, cancellationToken));
        }

        var result = await response.Content.ReadFromJsonAsync<AiChatThreadResponse>(cancellationToken);
        return (result?.Thread, null);
    }

    /// <summary>Thread única por id (200) — SPEC-20260922 RF-002.</summary>
    public async Task<AiChatThreadDto?> GetAiChatThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"/api/local/ai/threads/{Uri.EscapeDataString(threadId)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AiChatThreadResponse>(cancellationToken);
        return result?.Thread;
    }

    /// <summary>Remove thread + eventos + runs (204); false quando inexistente.</summary>
    public async Task<bool> DeleteAiChatThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync(
            $"/api/local/ai/threads/{Uri.EscapeDataString(threadId)}", cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>Snapshot REST dos eventos da thread (Accept: application/json).</summary>
    public async Task<IReadOnlyList<AiChatEventDto>> GetAiChatEventsAsync(
        string threadId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<AiChatEventListResponse>(
            $"/api/local/ai/threads/{Uri.EscapeDataString(threadId)}/events", cancellationToken);
        return response?.Events ?? [];
    }

    /// <summary>Posta um evento na thread (role: user/assistant/activity/error) — retorna (evento, erro).</summary>
    public async Task<(AiChatEventDto? Event, string? Error)> PostAiChatEventAsync(
        string threadId, AddAiChatEventRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/local/ai/threads/{Uri.EscapeDataString(threadId)}/events", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (null, await ReadErrorAsync(response, cancellationToken));
        }

        var result = await response.Content.ReadFromJsonAsync<AiChatEventResponse>(cancellationToken);
        return (result?.AiChatEvent, null);
    }

    /// <summary>Inicia um run de LLM sobre o histórico da thread (201) — retorna (run, erro).</summary>
    public async Task<(AiChatRunDto? Run, string? Error)> StartAiChatRunAsync(string threadId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"/api/local/ai/threads/{Uri.EscapeDataString(threadId)}/runs", content: null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (null, await ReadErrorAsync(response, cancellationToken));
        }

        var result = await response.Content.ReadFromJsonAsync<AiChatRunResponse>(cancellationToken);
        return (result?.Run, null);
    }

    /// <summary>Envia prompt para uma thread em modo agent (steer/queue) (202).</summary>
    public async Task<bool> PromptAgentThreadAsync(string threadId, string text, string delivery = "queue", CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/local/ai/threads/{Uri.EscapeDataString(threadId)}/prompt",
            new PromptAgentThreadRequest(text, delivery),
            cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>SPEC-20260921-ai-code-chat-ux RF-002: enfileira prompt FIFO (201) — retorna (evento "queued" persistido, erro).</summary>
    public async Task<(AiChatEventDto? Event, string? Error)> QueueAgentThreadPromptAsync(string threadId, string text, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/local/ai/threads/{Uri.EscapeDataString(threadId)}/queue",
            new PromptAgentThreadRequest(text, "queue"),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (null, await ReadErrorAsync(response, cancellationToken));
        }

        var result = await response.Content.ReadFromJsonAsync<AiChatEventResponse>(cancellationToken);
        return (result?.AiChatEvent, null);
    }

    /// <summary>RF-002: cancela um prompt enfileirado antes do dispatch (204).</summary>
    public async Task<bool> CancelQueuedPromptAsync(string threadId, string eventId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync(
            $"/api/local/ai/threads/{Uri.EscapeDataString(threadId)}/queue/{Uri.EscapeDataString(eventId)}",
            cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>RF-004: cria uma thread-fork copiando eventos até o evento selecionado (201) — retorna (thread, erro).</summary>
    public async Task<(AiChatThreadDto? Thread, string? Error)> ForkAiChatThreadAsync(string threadId, string eventId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/local/ai/threads/{Uri.EscapeDataString(threadId)}/fork",
            new ForkAiChatThreadRequest(eventId),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (null, await ReadErrorAsync(response, cancellationToken));
        }

        var result = await response.Content.ReadFromJsonAsync<AiChatThreadResponse>(cancellationToken);
        return (result?.Thread, null);
    }

    /// <summary>RF-004: reenvia o último prompt do usuário (202); cancela o turno ativo antes.</summary>
    public async Task<bool> RetryAgentThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"/api/local/ai/threads/{Uri.EscapeDataString(threadId)}/retry",
            content: null,
            cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>Envia cancelamento para uma thread em modo agent (204).</summary>
    public async Task<bool> CancelAgentThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"/api/local/ai/threads/{Uri.EscapeDataString(threadId)}/cancel",
            content: null,
            cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>Responde a uma solicitação de permissão de ferramenta (allow/deny/always) (204).</summary>
    public async Task<bool> ReplyAgentPermissionAsync(string threadId, string requestId, string outcome, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/local/ai/threads/{Uri.EscapeDataString(threadId)}/permissions/{Uri.EscapeDataString(requestId)}/reply",
            new PermissionReplyRequest(outcome),
            cancellationToken);
        return response.IsSuccessStatusCode;
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
        var snapshot = await GetConfigurationSnapshotAsync(cancellationToken);
        return snapshot?.Entries ?? [];
    }

    /// <summary>
    /// SPEC-20261010-settings-configuration-tab: catálogo + resumo de
    /// conexões (provider do banco e modo do cache) numa só chamada.
    /// </summary>
    public async Task<ConfigurationEntriesResponse?> GetConfigurationSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<ConfigurationEntriesResponse>("/api/configuration", cancellationToken);
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

    /// <summary>SPEC-20261005 RF-009: VAPID public key for pushManager.subscribe
    /// (GET /api/local/push/vapid-public — generates the pair on first call).</summary>
    public async Task<string?> GetPushVapidPublicKeyAsync(CancellationToken cancellationToken = default)
    {
        var body = await _httpClient.GetFromJsonAsync<JsonObject>("/api/local/push/vapid-public", cancellationToken);
        return body?["publicKey"]?.GetValue<string>();
    }

    /// <summary>Lista os jobs gerenciados com estado, schedule efetivo e log recente (SPEC-20260929-jobs-dashboard).</summary>
    public async Task<IReadOnlyList<JobStatusDto>> GetJobsAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<List<JobStatusDto>>("/api/jobs", cancellationToken) ?? [];

    /// <summary>Persiste override de enabled/intervalo do job. Retorna erro da API ou <c>null</c> em sucesso.</summary>
    public async Task<string?> UpdateJobAsync(
        string key, bool? enabled, int? intervalSeconds, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync(
            $"/api/jobs/{Uri.EscapeDataString(key)}",
            new UpdateJobRequest(enabled, intervalSeconds),
            cancellationToken);
        return response.IsSuccessStatusCode ? null : await ReadErrorMessageAsync(response, cancellationToken);
    }

    /// <summary>Dispara execução imediata do job (202). Retorna erro da API ou <c>null</c>.</summary>
    public async Task<string?> RunJobNowAsync(string key, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"/api/jobs/{Uri.EscapeDataString(key)}/run", content: null, cancellationToken);
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

    /// <summary>
    /// Teste de conectividade do servidor RAG MCP — handshake real tools/list
    /// (SPEC-20261003-ops-hardening RF-003). Sempre 200; <c>ok</c>/<c>error</c> no payload.
    /// </summary>
    public async Task<RagTestResult?> TestRagConnectionAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync("/api/mcp/rag/test", content: null, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<RagTestResult>(cancellationToken)
            : new RagTestResult(false, null, null, await ReadErrorMessageAsync(response, cancellationToken));
    }

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

    /// <summary>
    /// MCP servers merged for the AI Code chat (config ∪ ~/.agents ∪ rag) —
    /// SPEC-20261010-mcp-skills-hub.
    /// </summary>
    public async Task<IReadOnlyList<ChatMcpServerStatus>> GetChatMcpServersAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<IReadOnlyList<ChatMcpServerStatus>>("/api/mcp/chat", cancellationToken)
        ?? [];

    /// <summary>Per-agent-CLI MCP inventory (name + transport per server).</summary>
    public async Task<IReadOnlyList<AgentMcpInventoryDto>> GetAgentMcpInventoryAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<IReadOnlyList<AgentMcpInventoryDto>>("/api/mcp/agents", cancellationToken)
        ?? [];

    /// <summary>Installs an MCP server into the selected agent CLIs. Returns per-agent results or the API error.</summary>
    public async Task<(IReadOnlyList<McpAgentResult>? Results, string? Error)> InstallAgentMcpAsync(
        AgentMcpInstallRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/mcp/agents/install", request, cancellationToken);
        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<List<McpAgentResult>>(cancellationToken), null)
            : (null, await ReadErrorMessageAsync(response, cancellationToken));
    }

    /// <summary>Removes an MCP server from the selected agent CLIs.</summary>
    public async Task<(IReadOnlyList<McpAgentResult>? Results, string? Error)> RemoveAgentMcpAsync(
        AgentMcpRemoveRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/mcp/agents/remove", request, cancellationToken);
        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<List<McpAgentResult>>(cancellationToken), null)
            : (null, await ReadErrorMessageAsync(response, cancellationToken));
    }

    /// <summary>skills.sh search via <c>npx skills find</c> (max 20 rows).</summary>
    public async Task<IReadOnlyList<SkillSearchResultDto>> SearchSkillsAsync(
        string query, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"/api/skills/search?q={Uri.EscapeDataString(query)}", cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<List<SkillSearchResultDto>>(cancellationToken) ?? []
            : [];
    }

    /// <summary>Installs a skills repository (optional per-run override) — 202.</summary>
    public async Task<(SkillsInstallStatus? Status, string? Error)> InstallSkillsRepositoryAsync(
        string? repository, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "/api/skills/install-repo",
            new SkillRepoInstallRequest(repository),
            cancellationToken);
        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<SkillsInstallStatus>(cancellationToken), null)
            : (null, await ReadErrorMessageAsync(response, cancellationToken));
    }

    /// <summary>Installs a single skill — <c>repo[@skill]</c>. Returns the step + API error.</summary>
    public async Task<(SkillsInstallStep? Step, string? Error)> InstallSkillAsync(
        string repository, string? skill, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "/api/skills/install-one",
            new SkillInstallRequest(repository, skill),
            cancellationToken);
        var step = await response.Content.ReadFromJsonAsync<SkillsInstallStep>(cancellationToken);
        return response.IsSuccessStatusCode
            ? (step, null)
            : (step, await ReadErrorMessageAsync(response, cancellationToken));
    }

    /// <summary>Status dos CLIs de agente (instalado/versão/auth) — SPEC-20260917-cli-agents-terminal.</summary>
    public async Task<IReadOnlyList<AgentCliStatus>> GetAgentClisAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<IReadOnlyList<AgentCliStatus>>("/api/agent-clis", cancellationToken)
        ?? [];

    /// <summary>Estado do refresh em background das sondas de CLI — SPEC-20260928.</summary>
    public async Task<AgentCliRefreshStatusDto?> GetAgentCliRefreshStatusAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<AgentCliRefreshStatusDto>("/api/agent-clis/refresh", cancellationToken);

    /// <summary>Dispara o refresh em background das sondas de CLI — SPEC-20260928.</summary>
    public async Task<bool> RefreshAgentClisAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync("/api/agent-clis/refresh", content: null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Builtins cujo executável resolve no PATH — sem filtro de auth/enabled
    /// (SPEC-20260928-ai-code-generic-cli RF-001; PTY threads só exigem o binário).
    /// </summary>
    public async Task<IReadOnlyList<AgentInfo>> GetInstalledAgentsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<InstalledAgentsResponse>(
            "/api/agents/installed", cancellationToken);
        return response?.Agents ?? [];
    }

    /// <summary>SPEC-20261006 RF-001: agent dashboard columns (scope = conversation; default 'harness').</summary>
    public async Task<AgentDashboardDto?> GetDelegationDashboardAsync(
        string? scope = null, CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<AgentDashboardDto>(
            $"/api/local/delegation/dashboard{(string.IsNullOrWhiteSpace(scope) ? "" : $"?scope={Uri.EscapeDataString(scope)}")}",
            cancellationToken);

    /// <summary>SPEC-20261007 RF-001: reply to a mailbox message (answers escalations/decisions).</summary>
    public async Task<string?> ReplyMailboxAsync(
        string messageId, string body, string? scope = null, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/local/delegation/mailbox/{Uri.EscapeDataString(messageId)}/reply{(string.IsNullOrWhiteSpace(scope) ? "" : $"?scope={Uri.EscapeDataString(scope)}")}",
            new { body }, cancellationToken);
        return response.IsSuccessStatusCode
            ? null
            : await ReadErrorAsync(response, cancellationToken);
    }

    /// <summary>SPEC-20261007 RF-001: dismiss a mailbox message without replying.</summary>
    public async Task<string?> DismissMailboxAsync(
        string messageId, string? scope = null, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"/api/local/delegation/mailbox/{Uri.EscapeDataString(messageId)}/dismiss{(string.IsNullOrWhiteSpace(scope) ? "" : $"?scope={Uri.EscapeDataString(scope)}")}",
            content: null, cancellationToken);
        return response.IsSuccessStatusCode
            ? null
            : await ReadErrorAsync(response, cancellationToken);
    }

    /// <summary>SPEC-20261007 RF-004: per-leg diffstat + bounded patch of a fan-out group.</summary>
    public async Task<IReadOnlyList<FanoutCompareLegDto>> GetFanoutCompareAsync(
        string groupId, string? scope = null, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<FanoutCompareResponse>(
            $"/api/local/delegation/fanout/{Uri.EscapeDataString(groupId)}/compare{(string.IsNullOrWhiteSpace(scope) ? "" : $"?scope={Uri.EscapeDataString(scope)}")}",
            cancellationToken);
        return response?.Legs ?? [];
    }

    /// <summary>
    /// SPEC-20261009 RF-002: promote a worktree leg — commit + push its branch.
    /// SPEC-20261004-promote-leg-pr: <paramref name="createPr"/> also opens the PR.
    /// </summary>
    public async Task<(PromoteResultDto? Result, string? Error)> PromoteDelegationTaskAsync(
        string taskId, string? scope = null, bool createPr = false,
        string? title = null, string? baseBranch = null,
        CancellationToken cancellationToken = default)
    {
        var query = string.IsNullOrWhiteSpace(scope) ? "" : $"scope={Uri.EscapeDataString(scope)}";
        if (createPr)
        {
            query += string.IsNullOrEmpty(query) ? "createPr=true" : "&createPr=true";
        }
        if (title is not null)
        {
            query += $"{(query.Length == 0 ? "" : "&")}title={Uri.EscapeDataString(title)}";
        }
        if (baseBranch is not null)
        {
            query += $"{(query.Length == 0 ? "" : "&")}baseBranch={Uri.EscapeDataString(baseBranch)}";
        }

        var response = await _httpClient.PostAsync(
            $"/api/local/delegation/tasks/{Uri.EscapeDataString(taskId)}/promote{(query.Length == 0 ? "" : $"?{query}")}",
            content: null, cancellationToken);
        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<PromoteResultDto>(cancellationToken), null)
            : (null, await ReadErrorAsync(response, cancellationToken));
    }

    /// <summary>SPEC-20261009 RF-003: delegate a board issue into the DAG (worktree leg).</summary>
    public async Task<(DelegationTaskDto? Task, string? Error)> DelegateIssueAsync(
        DelegateIssueRequest request, string? scope = null, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/local/delegation/tasks/from-issue{(string.IsNullOrWhiteSpace(scope) ? "" : $"?scope={Uri.EscapeDataString(scope)}")}",
            request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (null, await ReadErrorAsync(response, cancellationToken));
        }

        var payload = await response.Content.ReadFromJsonAsync<DelegateIssueResponse>(cancellationToken);
        return (payload?.Task, null);
    }

    /// <summary>SPEC-20261009 RF-004: merged activity feed (task lifecycle + mailbox).</summary>
    public async Task<IReadOnlyList<DelegationEventDto>> GetDelegationEventsAsync(
        string? scope = null, int take = 30, CancellationToken cancellationToken = default)
    {
        var url = $"/api/local/delegation/events?take={take}"
            + (string.IsNullOrWhiteSpace(scope) ? "" : $"&scope={Uri.EscapeDataString(scope)}");
        var response = await _httpClient.GetFromJsonAsync<DelegationEventsResponse>(url, cancellationToken);
        return response?.Events ?? [];
    }

    /// <summary>SPEC-20261006 RF-002/RF-003: resumable on-disk CLI sessions.
    /// SPEC-20261010-agents-page-tabs RF-003/RF-004: `cli` + `take` filters.</summary>
    public async Task<IReadOnlyList<AgentSessionInfoDto>> GetAgentSessionsAsync(
        string? cli = null, int? take = null, CancellationToken cancellationToken = default)
    {
        var query = string.Join('&', new[]
            {
                string.IsNullOrWhiteSpace(cli) ? null : $"cli={Uri.EscapeDataString(cli)}",
                take is null ? null : $"take={take}",
            }.Where(p => p is not null));
        var response = await _httpClient.GetFromJsonAsync<AgentSessionsResponse>(
            $"/api/agents/sessions{(query.Length == 0 ? "" : $"?{query}")}",
            cancellationToken);
        return response?.Sessions ?? [];
    }

    /// <summary>Defs de CLI customizadas (SPEC-20260928-ai-code-generic-cli RF-002).</summary>
    public async Task<IReadOnlyList<AgentCliDefinitionDto>> GetCustomAgentClisAsync(
        CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<IReadOnlyList<AgentCliDefinitionDto>>(
            "/api/agents/custom", cancellationToken) ?? [];

    /// <summary>Cria uma def customizada — retorna (def, erro) com o detail do payload.</summary>
    public async Task<(AgentCliDefinitionDto? Def, string? Error)> CreateCustomAgentCliAsync(
        UpsertAgentCliDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/agents/custom", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (null, await ReadErrorAsync(response, cancellationToken));
        }

        return (await response.Content.ReadFromJsonAsync<AgentCliDefinitionDto>(cancellationToken), null);
    }

    /// <summary>Atualiza uma def customizada — retorna (def, erro); erro "not-found" em 404.</summary>
    public async Task<(AgentCliDefinitionDto? Def, string? Error)> UpdateCustomAgentCliAsync(
        string id, UpsertAgentCliDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync(
            $"/api/agents/custom/{Uri.EscapeDataString(id)}", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (null, await ReadErrorAsync(response, cancellationToken));
        }

        return (await response.Content.ReadFromJsonAsync<AgentCliDefinitionDto>(cancellationToken), null);
    }

    /// <summary>Remove uma def customizada (204); false quando inexistente.</summary>
    public async Task<bool> DeleteCustomAgentCliAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync(
            $"/api/agents/custom/{Uri.EscapeDataString(id)}", cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Containers Docker em execução + CLIs detectadas dentro (SPEC-20260928
    /// RF-004). Null quando o daemon está indisponível (503) — o chamador
    /// degrada para "host only" sem erro.
    /// </summary>
    public async Task<IReadOnlyList<DockerContainerDto>?> GetDockerContainersAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("/api/agents/docker/containers", cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<IReadOnlyList<DockerContainerDto>>(cancellationToken)
            : null;
    }

    /// <summary>Métricas compactas por CLI (sessions/tokens no período) — SPEC-20260919-cli-metrics.</summary>
    public async Task<CliMetricsSummaryDto?> GetCliMetricsSummaryAsync(
        string period = "7d", CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<CliMetricsSummaryDto>(
            $"/api/local/cli-metrics/summary?period={Uri.EscapeDataString(period)}", cancellationToken);

    /// <summary>Status de ingestão por fonte (badge de saúde) — SPEC-20260919-cli-metrics.</summary>
    public async Task<IReadOnlyList<CliMetricSourceDto>> GetCliMetricSourcesAsync(
        CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<IReadOnlyList<CliMetricSourceDto>>(
            "/api/local/cli-metrics/sources", cancellationToken) ?? [];

    /// <summary>Sync manual sob demanda — SPEC-20260919-cli-metrics.</summary>
    public async Task<CliMetricsSyncResultDto?> SyncCliMetricsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync("/api/local/cli-metrics/sync", content: null, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<CliMetricsSyncResultDto>(cancellationToken)
            : null;
    }

    /// <summary>Summary FinOps agregado (E14) — `last-7-days` | `last-30-days` | `all`.</summary>
    public async Task<FinOpsSummaryDto?> GetFinOpsSummaryAsync(
        string period = "last-30-days", CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<FinOpsSummaryDto>(
            $"/api/harness/finops/summary?period={Uri.EscapeDataString(period)}", cancellationToken);

    /// <summary>Telemetria detalhada de um run (E14); null quando sem métricas.</summary>
    public async Task<RunTelemetryDto?> GetRunTelemetryAsync(
        string runId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"/api/harness/runs/{Uri.EscapeDataString(runId)}/telemetry", cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<RunTelemetryDto>(cancellationToken)
            : null;
    }

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

    /// <summary>Reinicia o code-server (kill → spawn → wait-listening) — SPEC-20260920 RF-008.</summary>
    public async Task<Taskboard.Application.Contracts.Vscode.VscodeStatus?> RestartVscodeAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync("/api/vscode/restart", null, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<Taskboard.Application.Contracts.Vscode.VscodeStatus>(cancellationToken)
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

    /// <summary>Catálogo de living specs (E13) — filtro por status e texto.
    /// <paramref name="repo"/> seleciona o .specs do clone (SPEC-20260920 RF-005).</summary>
    public async Task<IReadOnlyList<LivingSpecDto>> GetSpecsAsync(
        string? status = null, string? query = null, string? repo = null,
        CancellationToken cancellationToken = default)
    {
        var url = "/api/specs";
        var sep = '?';
        if (!string.IsNullOrWhiteSpace(status))
        {
            url += $"?status={Uri.EscapeDataString(status)}";
            sep = '&';
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            url += $"{sep}q={Uri.EscapeDataString(query)}";
            sep = '&';
        }
        if (!string.IsNullOrWhiteSpace(repo))
        {
            url += $"{sep}repo={Uri.EscapeDataString(repo)}";
        }
        return await _httpClient.GetFromJsonAsync<List<LivingSpecDto>>(url, cancellationToken) ?? [];
    }

    /// <summary>Spec completa incluindo markdown bruto; null quando inexistente.</summary>
    public async Task<LivingSpecDetailDto?> GetSpecAsync(
        string specId, string? repo = null, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"/api/specs/{Uri.EscapeDataString(specId)}{RepoQuery(repo)}", cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<LivingSpecDetailDto>(cancellationToken)
            : null;
    }

    /// <summary>Atualiza a célula Status do metadata da spec; null quando inexistente.</summary>
    public async Task<LivingSpecDetailDto?> UpdateSpecStatusAsync(
        string specId, string status, string? repo = null, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/specs/{Uri.EscapeDataString(specId)}/status{RepoQuery(repo)}",
            new SpecStatusUpdateRequest(status), cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<LivingSpecDetailDto>(cancellationToken)
            : null;
    }

    /// <summary>Relatório de spec drift (E13 RF-002).</summary>
    public async Task<SpecDriftReportDto?> GetSpecDriftReportAsync(
        string? repo = null, CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<SpecDriftReportDto>(
            $"/api/specs/drift-report{RepoQuery(repo)}", cancellationToken);

    private static string RepoQuery(string? repo) =>
        string.IsNullOrWhiteSpace(repo) ? "" : $"?repo={Uri.EscapeDataString(repo)}";

    /// <summary>Templates de pipeline multi-agente (E12).</summary>
    public async Task<IReadOnlyList<PipelineTemplateDto>> GetPipelineTemplatesAsync(
        CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<List<PipelineTemplateDto>>(
            "/api/harness/pipelines/templates", cancellationToken) ?? [];

    /// <summary>Dispara uma execução de pipeline (201); null em erro.</summary>
    public async Task<PipelineExecutionDto?> StartPipelineAsync(
        PipelineStartRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "/api/harness/pipelines/start", request, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<PipelineExecutionDto>(cancellationToken)
            : null;
    }

    // SPEC-20260919-ade-cockpit-hitl §5 — runs (pipeline executions) para o cockpit.

    /// <summary>Pipeline templates disponíveis para iniciar um run.</summary>
    public async Task<IReadOnlyList<PipelineTemplateDto>> ListPipelineTemplatesAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<List<PipelineTemplateDto>>("/api/harness/pipelines/templates", cancellationToken) ?? [];

    /// <summary>Runs recentes (pipeline executions), mais novos primeiro.</summary>
    public async Task<IReadOnlyList<PipelineExecutionDto>> ListRunsAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<List<PipelineExecutionDto>>("/api/harness/runs", cancellationToken) ?? [];

    /// <summary>Inicia um run (pipeline) para o repositório.</summary>
    public async Task<PipelineExecutionDto?> StartRunAsync(RunStartRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/harness/runs", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PipelineExecutionDto>(cancellationToken);
    }

    /// <summary>Snapshot do run: execução + telemetria + worktree.</summary>
    public async Task<RunDetailsDto?> GetRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/harness/runs/{Uri.EscapeDataString(runId)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RunDetailsDto>(cancellationToken);
    }

    /// <summary>
    /// Eventos do run (replay para reconexão/join tardio) — SPEC-20260923
    /// RF-004: o endpoint serve o stream persistido + chunks live-only no
    /// envelope <see cref="CockpitEventsPage"/>.
    /// </summary>
    public async Task<IReadOnlyList<CockpitEventDto>> GetRunEventsAsync(string runId, CancellationToken cancellationToken = default) =>
        (await _httpClient.GetFromJsonAsync<CockpitEventsPage>(
            $"/api/harness/runs/{Uri.EscapeDataString(runId)}/events", cancellationToken))?.Events ?? [];

    /// <summary>
    /// Eventos normalizados de um escopo (run/thread/issue) —
    /// SPEC-20260921-board-cockpit-agent-observability.
    /// </summary>
    public async Task<AgentEventsPage> GetAgentEventsAsync(
        string scopeKind, string scopeId, long after = 0, int take = 500, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<AgentEventsPage>(
            $"/api/agents/events?scopeKind={Uri.EscapeDataString(scopeKind)}&scopeId={Uri.EscapeDataString(scopeId)}&after={after}&take={take}",
            cancellationToken);
        return response ?? new AgentEventsPage([], after, HasMore: false);
    }

    /// <summary>Ação de controle unificada (cancel/steer/retry) por escopo.</summary>
    public async Task<bool> PostAgentControlAsync(
        AgentControlRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/agents/control", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>Reply de permissão unificado por escopo.</summary>
    public async Task<bool> PostAgentPermissionReplyAsync(
        AgentPermissionReplyRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/agents/permissions/reply", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>Snapshot de estado do escopo (reconnect/mount).</summary>
    public async Task<AgentScopeState?> GetAgentScopeStateAsync(
        string scopeKind, string scopeId, CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<AgentScopeState>(
            $"/api/agents/state?scopeKind={Uri.EscapeDataString(scopeKind)}&scopeId={Uri.EscapeDataString(scopeId)}",
            cancellationToken);

    /// <summary>Página de eventos normalizados — espelha o envelope do endpoint.</summary>
    public sealed record AgentEventsPage(List<AgentExecutionEvent> Events, long NextAfter, bool HasMore);

    /// <summary>Envia instrução de steer — enfileirada para a próxima etapa (RF-003).</summary>
    public async Task SteerRunAsync(string runId, string instruction, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/harness/runs/{Uri.EscapeDataString(runId)}/steer",
            new SteerRequest(instruction), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Responde um gate de aprovação (`stage:` requestIds) — Allow ou Deny (RF-004).</summary>
    public async Task<PipelineExecutionDto?> RespondRunApprovalAsync(
        string runId, string requestId, string action, string? comment, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/harness/runs/{Uri.EscapeDataString(runId)}/approvals/{Uri.EscapeDataString(requestId)}",
            new ApprovalReplyRequest(action, comment), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PipelineExecutionDto>(cancellationToken);
    }

    /// <summary>Pausa o run entre estágios (SPEC-20260920-cockpit-pause-resume).</summary>
    public async Task<PipelineExecutionDto?> PauseRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/harness/runs/{Uri.EscapeDataString(runId)}/pause", (object?)null, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PipelineExecutionDto>(cancellationToken);
    }

    /// <summary>Resume um run pausado — despacha os estágios pendentes.</summary>
    public async Task<PipelineExecutionDto?> ResumeRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/harness/runs/{Uri.EscapeDataString(runId)}/resume", (object?)null, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PipelineExecutionDto>(cancellationToken);
    }

    /// <summary>Cancela o run (delega para o cancel do pipeline).</summary>
    public async Task<PipelineExecutionDto?> CancelRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/harness/pipelines/{Uri.EscapeDataString(runId)}/cancel", (object?)null, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PipelineExecutionDto>(cancellationToken);
    }

    /// <summary>Commita pendências, dá push na branch do worktree e abre o PR (RF-005).</summary>
    public async Task<string?> CreateRunPullRequestAsync(string runId, string title, string? body, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/harness/runs/{Uri.EscapeDataString(runId)}/create-pr",
            new CreatePrRequest(title, body), cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreatePrResponse>(cancellationToken);
        return result?.PrUrl;
    }

    /// <summary>Diff do worktree do run contra a branch base.</summary>
    public async Task<WorkspaceDiffDto?> GetWorktreeDiffAsync(string runId, CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<WorkspaceDiffDto>(
            $"/api/harness/worktrees/{Uri.EscapeDataString(runId)}/diff", cancellationToken);

    /// <summary>Lista filhos de um diretório do worktree (explorer RF-003).</summary>
    public async Task<WorktreeListDto?> GetWorktreeFilesAsync(string runId, string? path, CancellationToken cancellationToken = default)
    {
        var query = string.IsNullOrEmpty(path) ? string.Empty : $"?path={Uri.EscapeDataString(path)}";
        var response = await _httpClient.GetAsync(
            $"/api/harness/worktrees/{Uri.EscapeDataString(runId)}/files{query}", cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<WorktreeListDto>(cancellationToken)
            : null;
    }

    /// <summary>Conteúdo de um arquivo do worktree (explorer RF-003).</summary>
    public async Task<WorktreeFileContentDto?> GetWorktreeFileContentAsync(string runId, string path, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"/api/harness/worktrees/{Uri.EscapeDataString(runId)}/files/content?path={Uri.EscapeDataString(path)}",
            cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<WorktreeFileContentDto>(cancellationToken)
            : null;
    }

    private sealed record CreatePrResponse(string PrUrl);

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await ReadErrorAsync(response, cancellationToken);

    /// <summary>
    /// Extrai o motivo real de uma resposta de erro — `detail`/`code` do
    /// problem+json ou `error.message` do envelope inline
    /// (SPEC-20260922-ai-chat-command-bar RF-003).
    /// </summary>
    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string? body = null;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Best-effort — failure here is non-fatal.
        }

        return ProblemDetailReader.TryRead(body)
            ?? $"Request failed with status {(int)response.StatusCode}.";
    }

    private sealed record IssueHistoryResponse(List<Taskboard.GitHub.IssueHistoryItemDto> Items);

    private sealed record InstalledAgentsResponse(List<AgentInfo> Agents);

    private sealed record AgentSessionsResponse(List<AgentSessionInfoDto> Sessions);

    private sealed record FanoutCompareResponse(string GroupId, List<FanoutCompareLegDto> Legs);

    private sealed record DelegateIssueResponse(DelegationTaskDto Task);

    private sealed record DelegationEventsResponse(List<DelegationEventDto> Events);

    /// <summary>SPEC-20261009 RF-002: promote result — pushed branch + commit (+ PR URL when requested).</summary>
    public sealed record PromoteResultDto(
        string TaskId, string Branch, string CommitSha, string? PullRequestUrl = null);

    // SPEC-20260929-ai-code-provider-chat.
    public async Task<IReadOnlyList<ChatProviderDto>> GetChatProvidersAsync(CancellationToken cancellationToken = default)
    {
        var body = await _httpClient.GetFromJsonAsync<ChatProviderListResponse>("api/local/chat/providers", cancellationToken);
        return body?.Providers ?? [];
    }

    public async Task<ChatProviderDto> CreateChatProviderAsync(ChatProviderUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/local/chat/providers", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatProviderResponse>(cancellationToken: cancellationToken);
        return body!.Provider;
    }

    public async Task<ChatProviderDto> UpdateChatProviderAsync(Guid id, ChatProviderUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/local/chat/providers/{id}", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatProviderResponse>(cancellationToken: cancellationToken);
        return body!.Provider;
    }

    public async Task DeleteChatProviderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"api/local/chat/providers/{id}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<string>> GetChatProviderModelsAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"api/local/chat/providers/{providerId}/models", cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatModelListResponse>(cancellationToken: cancellationToken);
        return body!.Models;
    }

    public async Task<IReadOnlyList<ChatConversationDto>> GetChatConversationsAsync(
        string? query = null, bool archived = false, bool hasNegativeFeedback = false,
        CancellationToken cancellationToken = default)
    {
        var url = "api/local/chat/conversations?archived=" + (archived ? "true" : "false");
        if (!string.IsNullOrWhiteSpace(query))
        {
            url += $"&q={Uri.EscapeDataString(query)}";
        }

        // SPEC-20261005-chat-attachments-feedback RF-006: 👎 filter.
        if (hasNegativeFeedback)
        {
            url += "&hasNegativeFeedback=true";
        }

        var body = await _httpClient.GetFromJsonAsync<ChatConversationListResponse>(url, cancellationToken);
        return body?.Conversations ?? [];
    }

    public async Task<ChatConversationDto> CreateChatConversationAsync(CreateChatConversationRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/local/chat/conversations", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatConversationResponse>(cancellationToken: cancellationToken);
        return body!.Conversation;
    }

    public async Task<ChatConversationDetailDto?> GetChatConversationAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"api/local/chat/conversations/{Uri.EscapeDataString(id)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatConversationDetailDto>(cancellationToken: cancellationToken);
    }

    // SPEC-20261011-chat-workspace-panel RF-003/RF-004/RF-005/RF-008: workspace
    // pane read APIs — null on 404 (unknown/deleted conversation).
    public async Task<ConversationWorkspaceDto?> GetChatWorkspaceAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"api/local/chat/conversations/{Uri.EscapeDataString(conversationId)}/workspace", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ConversationWorkspaceResponse>(cancellationToken: cancellationToken);
        return body?.Workspace;
    }

    public async Task<IReadOnlyList<ChatTodoItem>> GetChatTodosAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"api/local/chat/conversations/{Uri.EscapeDataString(conversationId)}/todos", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatTodoListResponse>(cancellationToken: cancellationToken);
        return body?.Todos ?? [];
    }

    public async Task<WorkspaceDiffDto?> GetChatWorkspaceDiffAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"api/local/chat/conversations/{Uri.EscapeDataString(conversationId)}/diff", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ConversationDiffResponse>(cancellationToken: cancellationToken);
        return body?.Diff;
    }

    public async Task<ChatApprovalDto?> GetChatPlanAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"api/local/chat/conversations/{Uri.EscapeDataString(conversationId)}/plan", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ConversationPlanResponse>(cancellationToken: cancellationToken);
        return body?.Plan;
    }

    // SPEC-20261014-chat-git-bar-overview RF-001..RF-005: git chips + actions
    // for the conversation workspace.
    public async Task<ChatGitStatusDto?> GetChatGitStatusAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"api/local/chat/conversations/{Uri.EscapeDataString(conversationId)}/git/status", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatGitStatusResponse>(cancellationToken: cancellationToken);
        return body?.Status;
    }

    public async Task<ChatGitOpResult?> RunChatGitOpAsync(string conversationId, string op, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync($"api/local/chat/conversations/{Uri.EscapeDataString(conversationId)}/git/{op}", content: null, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatGitOpResponse>(cancellationToken: cancellationToken);
        return body?.Result;
    }

    public async Task<ChatCreatePrResult?> CreateChatPullRequestAsync(
        string conversationId, CreateChatPullRequestRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"api/local/chat/conversations/{Uri.EscapeDataString(conversationId)}/pull-request",
            request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatCreatePrResponse>(cancellationToken: cancellationToken);
        return body?.Result;
    }

    /// <summary>
    /// SPEC-20261015-chat-preview-panel RF-002: pins the conversation
    /// preview target (loopback URL or /preview/… path — the server
    /// normalizes; 400 surfaces as the response body).
    /// </summary>
    public async Task<ChatConversationDto?> SetChatPreviewAsync(
        string id, string url, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"api/local/chat/conversations/{Uri.EscapeDataString(id)}/preview",
            new SetChatPreviewRequest(url), cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatConversationResponse>(cancellationToken: cancellationToken);
        return body?.Conversation;
    }

    /// <summary>RF-002: clears the pinned preview target.</summary>
    public async Task<ChatConversationDto?> ClearChatPreviewAsync(
        string id, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync(
            $"api/local/chat/conversations/{Uri.EscapeDataString(id)}/preview", cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatConversationResponse>(cancellationToken: cancellationToken);
        return body?.Conversation;
    }

    /// <summary>RF-006: "app live" probe — HEAD through the preview proxy.</summary>
    public async Task<bool> ProbeChatPreviewAsync(string previewPath, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, previewPath);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>SPEC-20261017-chat-polish RF-001: post-run next-action chips —
    /// empty list on failure (fire-and-forget by contract).</summary>
    public async Task<IReadOnlyList<string>> GetChatSuggestionsAsync(
        string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsync(
                $"api/local/chat/conversations/{Uri.EscapeDataString(id)}/suggestions",
                content: null, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var body = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
            return body?["suggestions"]?.Deserialize<IReadOnlyList<string>>() ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>SPEC-20261016-chat-browser-tool RF-004: shots gallery feed.</summary>
    public async Task<IReadOnlyList<BrowserShotDto>> GetBrowserShotsAsync(
        string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var body = await _httpClient.GetFromJsonAsync<JsonObject>(
                $"api/local/chat/conversations/{Uri.EscapeDataString(id)}/browser/shots", cancellationToken);
            return body?["shots"]?.Deserialize<IReadOnlyList<BrowserShotDto>>() ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task DeleteChatConversationAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"api/local/chat/conversations/{Uri.EscapeDataString(id)}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task RenameChatConversationAsync(string id, string title, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PatchAsJsonAsync($"api/local/chat/conversations/{Uri.EscapeDataString(id)}", new PatchChatConversationRequest(Title: title), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>B-15: persiste o modelo selecionado na conversa ativa (PATCH Model).</summary>
    public async Task UpdateChatConversationModelAsync(string id, string model, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PatchAsJsonAsync($"api/local/chat/conversations/{Uri.EscapeDataString(id)}", new PatchChatConversationRequest(Model: model), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// SPEC-20261003-ai-code-agent-chat: persists the Agent-bar binding (CLI,
    /// repo, model) on the active conversation. <paramref name="agent"/> with
    /// all-null fields clears the binding.
    /// </summary>
    public async Task UpdateChatConversationAgentAsync(string id, ChatAgentContext agent, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PatchAsJsonAsync($"api/local/chat/conversations/{Uri.EscapeDataString(id)}", new PatchChatConversationRequest(Agent: agent), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// SPEC-20261005-chat-tool-approval RF-006: persists the permission preset
    /// (chat|ask|full) — takes effect on the next tool call.
    /// </summary>
    public async Task<ChatConversationDto?> UpdateChatConversationPresetAsync(
        string id, string preset, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PatchAsJsonAsync(
            $"api/local/chat/conversations/{Uri.EscapeDataString(id)}",
            new PatchChatConversationRequest(PermissionPreset: preset), cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatConversationResponse>(cancellationToken: cancellationToken);
        return body?.Conversation;
    }

    /// <summary>
    /// SPEC-20261005-chat-tool-approval RF-003: answers a pending approval.
    /// Returns the updated row, or null when it already decided (409).
    /// </summary>
    public async Task<ChatApprovalDto?> DecideChatApprovalAsync(
        string approvalId, string outcome, string? reason = null, bool rememberTool = false,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"api/local/chat/approvals/{Uri.EscapeDataString(approvalId)}/decide",
            new DecideChatApprovalRequest(outcome, reason, rememberTool), cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict
            || response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatApprovalResponse>(cancellationToken: cancellationToken);
        return body?.Approval;
    }

    /// <summary>
    /// SPEC-20261005-chat-plan-mode RF-001: toggles plan mode — mid-run safe;
    /// on→off also cancels a pending plan review.
    /// </summary>
    public async Task<ChatConversationDto?> SetChatPlanModeAsync(
        string id, bool active, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"api/local/chat/conversations/{Uri.EscapeDataString(id)}/plan-mode",
            new SetChatPlanModeRequest(active), cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatConversationResponse>(cancellationToken: cancellationToken);
        return body?.Conversation;
    }

    /// <summary>
    /// SPEC-20261005-chat-context-management P2: manual compaction — writes a
    /// persisted summary row the next run reuses.
    /// </summary>
    public async Task<ChatConversationDto?> CompactChatConversationAsync(
        string id, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"api/local/chat/conversations/{Uri.EscapeDataString(id)}/compact",
            content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatConversationResponse>(cancellationToken: cancellationToken);
        return body?.Conversation;
    }

    /// <summary>RF-007: approvals of a conversation — "pending" replays the unresolved card.</summary>
    public async Task<IReadOnlyList<ChatApprovalDto>> GetChatApprovalsAsync(
        string conversationId, string? status = null, CancellationToken cancellationToken = default)
    {
        var url = $"api/local/chat/conversations/{Uri.EscapeDataString(conversationId)}/approvals";
        if (!string.IsNullOrWhiteSpace(status))
        {
            url += $"?status={Uri.EscapeDataString(status)}";
        }

        var body = await _httpClient.GetFromJsonAsync<ChatApprovalListResponse>(url, cancellationToken);
        return body?.Approvals ?? [];
    }

    /// <summary>
    /// SPEC-20261005-chat-background-resume RF-002: queues a durable chat run
    /// (202 + run row) — the server executes it detached from this call.
    /// SPEC-20261005-chat-fork-steering RF-005: <paramref name="steer"/> asks
    /// the server to land the message in the live run's steer inbox instead —
    /// <c>Steered</c> reports whether that happened (it degrades to a normal
    /// queued send when no run is active).
    /// </summary>
    public async Task<(ChatRunDto Run, bool Steered, string? SteerId)> EnqueueChatMessageAsync(
        string id, string content, bool steer = false,
        IReadOnlyList<string>? attachmentIds = null,
        ChatElementQuote? quote = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"api/local/chat/conversations/{Uri.EscapeDataString(id)}/messages",
            new SendChatMessageRequest(content, steer, attachmentIds, quote), cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<EnqueueChatMessageResponse>(cancellationToken: cancellationToken);
        return (body!.Run, body.Steered, body.SteerId);
    }

    /// <summary>
    /// SPEC-20261005-chat-attachments-feedback RF-001: stages an upload —
    /// the row stays unbound until a message send references its id.
    /// </summary>
    public async Task<ChatAttachmentDto?> UploadChatAttachmentAsync(
        string id, string fileName, string contentType, Stream content,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(content);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        form.Add(fileContent, "file", fileName);
        var response = await _httpClient.PostAsync(
            $"api/local/chat/conversations/{Uri.EscapeDataString(id)}/attachments", form, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatAttachmentDto>(cancellationToken: cancellationToken);
    }

    /// <summary>RF-001: removes a still-staged attachment (bound rows 409).</summary>
    public async Task<bool> DeleteChatAttachmentAsync(
        string id, string attachmentId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync(
            $"api/local/chat/conversations/{Uri.EscapeDataString(id)}/attachments/{Uri.EscapeDataString(attachmentId)}",
            cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// RF-005: rates an assistant message — 409 returns the current row for
    /// the client to re-offer. Version is the row's CAS token.
    /// </summary>
    public async Task<(ChatFeedbackDto? Feedback, bool Conflict)> PutChatFeedbackAsync(
        string id, string messageId, PutChatMessageFeedbackRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync(
            $"api/local/chat/conversations/{Uri.EscapeDataString(id)}/messages/{Uri.EscapeDataString(messageId)}/feedback",
            request, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<ChatFeedbackDto>(cancellationToken: cancellationToken);
        return (body, response.StatusCode == System.Net.HttpStatusCode.Conflict);
    }

    /// <summary>RF-005: clears the rating on a message (204).</summary>
    public async Task DeleteChatFeedbackAsync(
        string id, string messageId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync(
            $"api/local/chat/conversations/{Uri.EscapeDataString(id)}/messages/{Uri.EscapeDataString(messageId)}/feedback",
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// SPEC-20261005-chat-fork-steering RF-002: branches the conversation at
    /// <paramref name="messageId"/> — returns the new fork conversation.
    /// </summary>
    public async Task<ChatConversationDto?> ForkChatConversationAsync(
        string id, string messageId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"api/local/chat/conversations/{Uri.EscapeDataString(id)}/fork",
            new ForkChatConversationRequest(messageId), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatConversationDto>(cancellationToken: cancellationToken);
    }

    /// <summary>
    /// SPEC-20261005-chat-fork-steering RF-007: withdraws an unclaimed steer —
    /// 204 on success; false on 409 (already claimed) or 404.
    /// </summary>
    public async Task<bool> CancelChatSteerAsync(
        string id, string steerId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"api/local/chat/conversations/{Uri.EscapeDataString(id)}/steer/{Uri.EscapeDataString(steerId)}/cancel",
            content: null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>RF-003: opens the attach stream of a chat run — chat.sync then live events.</summary>
    public async Task<HttpResponseMessage> OpenChatRunStreamAsync(
        string conversationId, string runId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"api/local/chat/conversations/{Uri.EscapeDataString(conversationId)}/runs/{Uri.EscapeDataString(runId)}/stream");
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <summary>RF-006: archive/unarchive — archived conversations leave the active list.</summary>
    public async Task<ChatConversationDto?> SetChatConversationArchivedAsync(
        string id, bool archived, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"api/local/chat/conversations/{Uri.EscapeDataString(id)}/{(archived ? "archive" : "unarchive")}",
            content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatConversationResponse>(cancellationToken: cancellationToken);
        return body?.Conversation;
    }

    public async Task StopChatAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync($"api/local/chat/conversations/{Uri.EscapeDataString(id)}/stop", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// SPEC-20261012-chat-run-controls: cooperative pause — 202 with the run
    /// row (status may still be running; it flips at the next boundary).
    /// False on 404/409 (run gone or already terminal).
    /// </summary>
    public async Task<bool> PauseChatRunAsync(
        string conversationId, string runId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"api/local/chat/conversations/{Uri.EscapeDataString(conversationId)}/runs/{Uri.EscapeDataString(runId)}/pause",
            content: null, cancellationToken);
        if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Conflict)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>
    /// SPEC-20261012-chat-run-controls: wakes the parked executor or
    /// re-queues an executor-less paused row. False on 404/409.
    /// </summary>
    public async Task<bool> ResumeChatRunAsync(
        string conversationId, string runId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"api/local/chat/conversations/{Uri.EscapeDataString(conversationId)}/runs/{Uri.EscapeDataString(runId)}/resume",
            content: null, cancellationToken);
        if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Conflict)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>
    /// SPEC-20261004 RF-001: subdirectory listing for the workspace picker —
    /// <paramref name="path"/> null means the workspace root (~/<repos>).
    /// Returns null on invalid/out-of-jail paths.
    /// </summary>
    public async Task<WorkspaceDirsDto?> GetWorkspaceDirsAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(path)
            ? "api/local/workspace/dirs"
            : $"api/local/workspace/dirs?path={Uri.EscapeDataString(path)}";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<WorkspaceDirsDto>(cancellationToken);
    }

    /// <summary>
    /// SPEC-20261004 RF-008: probed model list of a custom CLI def — []
    /// when the def declares no ModelListArgs or the probe came up empty.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetCustomCliModelsAsync(string defId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"api/local/agents/custom/{Uri.EscapeDataString(defId)}/models", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var body = await response.Content.ReadFromJsonAsync<CustomCliModelsResponse>(cancellationToken);
        return body?.Models ?? [];
    }

    /// <summary>SPEC-20261001-chat-capability-registry: invocable capabilities catalog.</summary>
    public async Task<IReadOnlyList<ChatCapability>> GetChatCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var body = await _httpClient.GetFromJsonAsync<ChatCapabilitiesResponse>("api/local/chat/capabilities", cancellationToken);
        return body?.Capabilities ?? [];
    }

    /// <summary>SPEC-20261004-cli-slash-commands: slash commands/skills of one
    /// CLI (AgentCliKind or AgentType name) for the composer palette.</summary>
    public async Task<IReadOnlyList<CliCommandDto>> GetCliCommandsAsync(string cli, CancellationToken cancellationToken = default)
    {
        var body = await _httpClient.GetFromJsonAsync<CliCommandsResponse>(
            $"/api/cli-commands?cli={Uri.EscapeDataString(cli)}", cancellationToken);
        return body?.Commands ?? [];
    }

    /// <summary>Command/skill body for injection (RF-003).</summary>
    public async Task<CliCommandDetailDto?> GetCliCommandDetailAsync(string cli, string name, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"/api/cli-commands/{Uri.EscapeDataString(cli)}/{Uri.EscapeDataString(name)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CliCommandResponse>(cancellationToken);
        return body?.Command;
    }

    // ---- SPEC-20261005-chat-jobs-schedule-search ----

    /// <summary>RF-003: active jobs of a conversation — the strip polls this.</summary>
    public async Task<IReadOnlyList<ChatJobDto>> GetChatJobsAsync(
        string conversationId, bool activeOnly, CancellationToken cancellationToken = default)
    {
        var url = $"api/local/chat/conversations/{Uri.EscapeDataString(conversationId)}/jobs"
            + (activeOnly ? "?active=true" : string.Empty);
        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var body = await response.Content.ReadFromJsonAsync<ChatJobListResponse>(cancellationToken);
        return body?.Jobs ?? [];
    }

    /// <summary>RF-002: kill a running job — false on 404/409.</summary>
    public async Task<bool> KillChatJobAsync(
        string conversationId, string jobId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"api/local/chat/conversations/{Uri.EscapeDataString(conversationId)}/jobs/{Uri.EscapeDataString(jobId)}/kill",
            content: null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>RF-005: conversation schedules.</summary>
    public async Task<IReadOnlyList<ChatScheduleDto>> GetChatSchedulesAsync(
        string conversationId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"api/local/chat/conversations/{Uri.EscapeDataString(conversationId)}/schedules", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var body = await response.Content.ReadFromJsonAsync<ChatScheduleListResponse>(cancellationToken);
        return body?.Schedules ?? [];
    }

    /// <summary>RF-004/RF-005: create a schedule — null body on 400/409.</summary>
    public async Task<ChatScheduleDto?> CreateChatScheduleAsync(
        string conversationId, CreateChatScheduleRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"api/local/chat/conversations/{Uri.EscapeDataString(conversationId)}/schedules",
            request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadFromJsonAsync<ChatScheduleResponse>(cancellationToken);
        return body?.Schedule;
    }

    /// <summary>RF-005: cancel/re-enable — false on 404.</summary>
    public async Task<bool> UpdateChatScheduleAsync(
        string conversationId, string scheduleId, PatchChatScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PatchAsJsonAsync(
            $"api/local/chat/conversations/{Uri.EscapeDataString(conversationId)}/schedules/{Uri.EscapeDataString(scheduleId)}",
            request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>RF-005: delete a schedule row.</summary>
    public async Task<bool> DeleteChatScheduleAsync(
        string conversationId, string scheduleId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync(
            $"api/local/chat/conversations/{Uri.EscapeDataString(conversationId)}/schedules/{Uri.EscapeDataString(scheduleId)}",
            cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>RF-008: FTS5 content search — grouped hits with &lt;mark&gt; snippets.</summary>
    public async Task<IReadOnlyList<ChatSearchHitDto>> SearchChatContentAsync(
        string query, string? conversationId, int limit, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var url = $"api/local/chat/search?q={Uri.EscapeDataString(query)}&limit={limit}";
        if (!string.IsNullOrWhiteSpace(conversationId))
        {
            url += $"&conversationId={Uri.EscapeDataString(conversationId)}";
        }

        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var body = await response.Content.ReadFromJsonAsync<ChatSearchHitsResponse>(cancellationToken);
        return body?.Hits ?? [];
    }

    private sealed record ChatJobListResponse(List<ChatJobDto> Jobs);
    private sealed record ChatScheduleListResponse(List<ChatScheduleDto> Schedules);
    private sealed record ChatScheduleResponse(ChatScheduleDto Schedule);
    private sealed record ChatSearchHitsResponse(List<ChatSearchHitDto> Hits);

    private sealed record ChatProviderListResponse(List<ChatProviderDto> Providers);
    private sealed record ChatProviderResponse(ChatProviderDto Provider);
    private sealed record ChatModelListResponse(List<string> Models);
    private sealed record ChatConversationListResponse(List<ChatConversationDto> Conversations);
    private sealed record ChatConversationResponse(ChatConversationDto Conversation);
    private sealed record ChatApprovalResponse(ChatApprovalDto Approval);
    private sealed record ChatApprovalListResponse(List<ChatApprovalDto> Approvals);
    private sealed record ConversationWorkspaceResponse(ConversationWorkspaceDto Workspace);
    private sealed record ChatTodoListResponse(List<ChatTodoItem> Todos);
    private sealed record ConversationDiffResponse(WorkspaceDiffDto? Diff);
    private sealed record ConversationPlanResponse(ChatApprovalDto? Plan);
    private sealed record ChatGitStatusResponse(ChatGitStatusDto Status);
    private sealed record ChatGitOpResponse(ChatGitOpResult Result);
    private sealed record ChatCreatePrResponse(ChatCreatePrResult Result);
    private sealed record ChatCapabilitiesResponse(List<ChatCapability> Capabilities);
    private sealed record CustomCliModelsResponse(List<string> Models);

    private sealed record AiChatThreadListResponse(List<AiChatThreadDto> Threads);
    private sealed record AiChatThreadResponse(AiChatThreadDto Thread);
    private sealed record AiChatCatalogResponse(List<AiChatModelDto> Models);
    private sealed record AiChatEventListResponse(List<AiChatEventDto> Events);
    private sealed record AiChatEventResponse(AiChatEventDto AiChatEvent);
    private sealed record AiChatRunResponse(AiChatRunDto Run);
    private sealed record SettingsResponse(SettingsDto Settings);
    private sealed record SkillsResponse(List<SkillDto> Skills);
    private sealed record SkillDetailResponse(SkillDetailDto Skill);
    public sealed record ConfigurationEntriesResponse(
        List<ConfigurationEntryDto> Entries,
        ConnectionInfoDto? Connections = null,
        CacheStatsDto? Cache = null);
    private sealed record SkillFileContentResponse(string Path, string Content);
    private sealed record CliCommandsResponse(List<CliCommandDto> Commands);
    private sealed record CliCommandResponse(CliCommandDetailDto Command);
}
