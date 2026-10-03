using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Taskboard.GitHub;
using Taskboard.Mcp.Services;
using Taskboard.Requests;

namespace Taskboard.Mcp.Tools;

[McpServerToolType]
public static class TaskboardTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    [McpServerTool(Name = "get_issue_history"), Description("Obtém a timeline unificada de uma issue do board GitHub — eventos do board (movimentação, edição, fechamento) + execuções de agente, mais recente primeiro. Leia antes de assumir uma issue para recuperar contexto de execuções anteriores.")]
    public static async Task<CallToolResult> GetIssueHistoryAsync(ITaskboardApiClient client, string issue_id, int? take = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(issue_id))
            {
                return Error("O campo 'issue_id' é obrigatório.");
            }

            var query = take is { } t ? $"?take={t}" : string.Empty;
            var result = await client.GetAsync(
                $"/api/github/issues/{Uri.EscapeDataString(issue_id)}/history{query}", cancellationToken);
            return Json(result?["items"] ?? new JsonArray());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "list_github_issue_comments"), Description("Lista os comentários de uma issue do GitHub (board GitHub, identificada por owner/repo/número) em ordem cronológica. Comentários carregam o contexto deixado por humanos e agentes anteriores.")]
    public static async Task<CallToolResult> ListGitHubIssueCommentsAsync(ITaskboardApiClient client, string owner, string repo, int number, int? take = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = take is { } t ? $"?take={t}" : string.Empty;
            var result = await client.GetAsync(
                $"/api/github/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/issues/{number}/comments{query}",
                cancellationToken);
            return Json(result?["comments"] ?? new JsonArray());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "add_github_issue_comment"), Description("Adiciona um comentário a uma issue do GitHub — o comentário é publicado no GitHub e vira contexto para o próximo agente ou etapa. Use ao concluir uma etapa para registrar o que foi feito e o resultado.")]
    public static async Task<CallToolResult> AddGitHubIssueCommentAsync(ITaskboardApiClient client, string owner, string repo, int number, string body, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return Error("O campo 'body' é obrigatório.");
            }

            var result = await client.PostAsync(
                $"/api/github/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/issues/{number}/comments",
                new AddIssueCommentRequest(body), cancellationToken);
            return Json(result?["comment"] ?? new JsonObject());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "cloud_status"), Description("Retorna o status da conexão com a nuvem.")]
    public static async Task<CallToolResult> CloudStatusAsync(ITaskboardApiClient client, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await client.GetAsync("/api/local/cloud-session", cancellationToken);
            return Json(result ?? new JsonObject());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "list_issues"), Description("Lista as issues do board de um repositório GitHub (owner/repo) com coluna, prioridade e labels — o estado atual do trabalho rastreado pelo harness.")]
    public static async Task<CallToolResult> ListIssuesAsync(ITaskboardApiClient client, string owner, string repo, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                return Error("Os campos 'owner' e 'repo' são obrigatórios.");
            }

            var result = await client.GetAsync(
                $"/api/github/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/issues", cancellationToken);
            return Json(result?["issues"] ?? new JsonArray());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "get_issue"), Description("Retorna o detalhe de uma issue do board (título, corpo, coluna, labels, prioridade) de um repositório GitHub.")]
    public static async Task<CallToolResult> GetIssueAsync(ITaskboardApiClient client, string owner, string repo, int number, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                return Error("Os campos 'owner' e 'repo' são obrigatórios.");
            }

            var result = await client.GetAsync(
                $"/api/github/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/issues/{number}", cancellationToken);
            return Json(result?["issue"] ?? new JsonObject());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "move_issue"), Description("Move uma issue entre colunas do board. Colunas: backlog, todo, in-progress, in-review, in-pullrequest, blocked, done, canceled. Passe 'old_column' para validar a origem (conflito de concorrência retorna erro estruturado).")]
    public static async Task<CallToolResult> MoveIssueAsync(ITaskboardApiClient client, string owner, string repo, int number, string new_column, string? old_column = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                return Error("Os campos 'owner' e 'repo' são obrigatórios.");
            }

            var target = ParseColumn(new_column);
            if (target is null)
            {
                return Error($"Coluna '{new_column}' inválida. Use: {ValidColumns}.");
            }

            GitHubBoardColumn? source = null;
            if (!string.IsNullOrWhiteSpace(old_column))
            {
                source = ParseColumn(old_column);
                if (source is null)
                {
                    return Error($"Coluna '{old_column}' inválida. Use: {ValidColumns}.");
                }
            }

            var result = await client.PutAsync(
                $"/api/github/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/issues/{number}/column",
                new UpdateGitHubIssueColumnRequest(source, target.Value), cancellationToken);
            return Json(result?["issue"] ?? new JsonObject());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "list_specs"), Description("Lista os SPECs vivos (.specs/*.md) conhecidos pelo harness, opcionalmente filtrados por status, texto e repositório.")]
    public static async Task<CallToolResult> ListSpecsAsync(ITaskboardApiClient client, string? status = null, string? q = null, string? repo = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(("status", status), ("q", q), ("repo", repo));
            var result = await client.GetAsync($"/api/specs{query}", cancellationToken);
            return Json(result ?? new JsonArray());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "get_spec"), Description("Lê o conteúdo e os metadados de um SPEC do harness pelo id (ex.: 'SPEC-20261003-sqlite-backup'). Somente arquivos de .specs — path traversal é rejeitado.")]
    public static async Task<CallToolResult> GetSpecAsync(ITaskboardApiClient client, string spec_id, string? repo = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(spec_id) || spec_id.Contains('/') || spec_id.Contains("..", StringComparison.Ordinal))
            {
                return Error("O campo 'spec_id' deve ser o id de um arquivo de .specs (ex.: 'SPEC-20261003-sqlite-backup').");
            }

            var query = BuildQuery(("repo", repo));
            var result = await client.GetAsync(
                $"/api/specs/{Uri.EscapeDataString(spec_id)}{query}", cancellationToken);
            return Json(result ?? new JsonObject());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "list_jobs"), Description("Lista os jobs agendados do harness (nome, estado, última execução, próximo tick) — o equivalente a 'taskctl' de inspeção de jobs.")]
    public static async Task<CallToolResult> ListJobsAsync(ITaskboardApiClient client, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await client.GetAsync("/api/jobs", cancellationToken);
            return Json(result ?? new JsonArray());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "list_runs"), Description("Lista as execuções de pipeline/agente do harness (mais recentes primeiro) — use 'take' para limitar.")]
    public static async Task<CallToolResult> ListRunsAsync(ITaskboardApiClient client, int? take = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = take is { } t ? $"?take={t}" : string.Empty;
            var result = await client.GetAsync($"/api/harness/runs{query}", cancellationToken);
            return Json(result ?? new JsonArray());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "get_run_status"), Description("Retorna o estado detalhado de uma execução do harness (estágios, telemetria, isolamento) pelo run_id.")]
    public static async Task<CallToolResult> GetRunStatusAsync(ITaskboardApiClient client, string run_id, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(run_id))
            {
                return Error("O campo 'run_id' é obrigatório.");
            }

            var result = await client.GetAsync(
                $"/api/harness/runs/{Uri.EscapeDataString(run_id)}", cancellationToken);
            return Json(result ?? new JsonObject());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private static string ValidColumns => string.Join(", ", GitHubBoardColumnExtensions.GetAllLabels());

    private static GitHubBoardColumn? ParseColumn(string value) =>
        GitHubBoardColumnExtensions.FromLabel(value.Trim().Replace('_', '-'));

    private static string BuildQuery(params (string Name, string? Value)[] parameters)
    {
        var parts = parameters
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value!)}")
            .ToList();
        return parts.Count == 0 ? string.Empty : $"?{string.Join("&", parts)}";
    }

    private static CallToolResult Json(JsonNode? node)
    {
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = node?.ToJsonString() ?? "{}" }],
        };
    }

    private static CallToolResult Error(string message)
    {
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = message }],
        };
    }

}
