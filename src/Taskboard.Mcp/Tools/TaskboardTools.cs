using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
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
