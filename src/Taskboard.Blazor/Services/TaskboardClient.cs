using System.Net.Http.Json;
using System.Text.Json;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Configuration;
using Taskboard.Application.Contracts.Mcp;
using Taskboard.Application.Contracts.Operations;
using Taskboard.Application.Contracts.Settings;
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

    /// <summary>Catálogo de modelos disponíveis para novas threads.</summary>
    public async Task<IReadOnlyList<AiChatModelDto>> GetAiChatCatalogAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<AiChatCatalogResponse>("/api/local/ai/catalog", cancellationToken);
        return response?.Models ?? [];
    }

    /// <summary>Cria uma thread de chat (201) e retorna o DTO.</summary>
    public async Task<AiChatThreadDto?> CreateAiChatThreadAsync(
        CreateAiChatThreadRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/local/ai/threads", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

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

    /// <summary>Posta um evento na thread (role: user/assistant/activity/error).</summary>
    public async Task<AiChatEventDto?> PostAiChatEventAsync(
        string threadId, AddAiChatEventRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/local/ai/threads/{Uri.EscapeDataString(threadId)}/events", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var result = await response.Content.ReadFromJsonAsync<AiChatEventResponse>(cancellationToken);
        return result?.AiChatEvent;
    }

    /// <summary>Inicia um run de LLM sobre o histórico da thread (201).</summary>
    public async Task<AiChatRunDto?> StartAiChatRunAsync(string threadId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"/api/local/ai/threads/{Uri.EscapeDataString(threadId)}/runs", content: null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var result = await response.Content.ReadFromJsonAsync<AiChatRunResponse>(cancellationToken);
        return result?.Run;
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

    /// <summary>Eventos buffered do run (replay para reconexão/join tardio).</summary>
    public async Task<IReadOnlyList<CockpitEventDto>> GetRunEventsAsync(string runId, CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<List<CockpitEventDto>>(
            $"/api/harness/runs/{Uri.EscapeDataString(runId)}/events", cancellationToken) ?? [];

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

    private sealed record CreatePrResponse(string PrUrl);

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

    private sealed record AiChatThreadListResponse(List<AiChatThreadDto> Threads);
    private sealed record AiChatThreadResponse(AiChatThreadDto Thread);
    private sealed record AiChatCatalogResponse(List<AiChatModelDto> Models);
    private sealed record AiChatEventListResponse(List<AiChatEventDto> Events);
    private sealed record AiChatEventResponse(AiChatEventDto AiChatEvent);
    private sealed record AiChatRunResponse(AiChatRunDto Run);
    private sealed record SettingsResponse(SettingsDto Settings);
    private sealed record SkillsResponse(List<SkillDto> Skills);
    private sealed record SkillDetailResponse(SkillDetailDto Skill);
    private sealed record ConfigurationEntriesResponse(List<ConfigurationEntryDto> Entries);
    private sealed record SkillFileContentResponse(string Path, string Content);
}
