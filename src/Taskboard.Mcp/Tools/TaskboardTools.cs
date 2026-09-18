using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Taskboard.Mcp.Services;
using Taskboard.Requests;
using Taskboard.ValueObjects;

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

    [McpServerTool(Name = "list_projects"), Description("Lista todos os projetos cadastrados.")]
    public static async Task<CallToolResult> ListProjectsAsync(ITaskboardApiClient client, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await client.GetAsync("/api/projects", cancellationToken);
            return Json(result?["projects"] ?? new JsonArray());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "get_project"), Description("Obtém um projeto pelo identificador.")]
    public static async Task<CallToolResult> GetProjectAsync(ITaskboardApiClient client, string project_id, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await client.GetAsync($"/api/projects/{project_id}", cancellationToken);
            return Json(result?["project"] ?? new JsonObject());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "create_project"), Description("Cria um novo projeto.")]
    public static async Task<CallToolResult> CreateProjectAsync(ITaskboardApiClient client, string name, string? id = null, string? workspace_path = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new CreateProjectRequest(id, name, workspace_path);
            var result = await client.PostAsync("/api/projects", request, cancellationToken);
            return Json(result?["project"] ?? new JsonObject());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "list_issues"), Description("Lista issues de um projeto.")]
    public static async Task<CallToolResult> ListIssuesAsync(ITaskboardApiClient client, string project_id, string? status = null, int? limit = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new List<string> { $"projectId={Uri.EscapeDataString(project_id)}" };
            if (!string.IsNullOrWhiteSpace(status))
            {
                query.Add($"status={Uri.EscapeDataString(status)}");
            }

            if (limit is { } l)
            {
                query.Add($"limit={l}");
            }

            var path = "/api/tasks?" + string.Join("&", query);
            var result = await client.GetAsync(path, cancellationToken);
            return Json(result ?? new JsonObject());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "get_issue"), Description("Obtém uma issue pelo identificador.")]
    public static async Task<CallToolResult> GetIssueAsync(ITaskboardApiClient client, string issue_id, CancellationToken cancellationToken = default)
    {
        try
        {
            var id = await ResolveIssueIdAsync(client, issue_id, cancellationToken);
            var result = await client.GetAsync($"/api/tasks/{id}", cancellationToken);
            return Json(result?["task"] ?? new JsonObject());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "create_issue"), Description("Cria uma nova issue em um projeto.")]
    public static async Task<CallToolResult> CreateIssueAsync(ITaskboardApiClient client, string project_id, string title, string? description = null, string? status = null, string? priority = null, IReadOnlyList<string>? labels = null, DateTime? start_date = null, DateTime? due_date = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return Error("O campo 'title' é obrigatório.");
            }

            var request = new CreateTaskRequest(
                project_id,
                title,
                description,
                status,
                priority,
                labels,
                null,
                null,
                start_date,
                due_date);
            var result = await client.PostAsync("/api/tasks", request, cancellationToken);
            return Json(result?["task"] ?? new JsonObject());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "update_issue"), Description("Atualiza uma issue existente.")]
    public static async Task<CallToolResult> UpdateIssueAsync(ITaskboardApiClient client, string issue_id, JsonElement changes, long? version = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var id = await ResolveIssueIdAsync(client, issue_id, cancellationToken);
            var current = await client.GetAsync($"/api/tasks/{id}", cancellationToken);
            var resolvedVersion = version ?? current?["task"]?["version"]?.GetValue<long>() ?? 0L;

            var patch = JsonSerializer.Deserialize<TaskPatch>(changes, JsonOptions);
            if (patch is null)
            {
                return Error("O campo 'changes' é inválido.");
            }

            var request = new UpdateTaskRequest(resolvedVersion, patch);
            var result = await client.PatchAsync($"/api/tasks/{id}", request, cancellationToken);
            return Json(result?["task"] ?? new JsonObject());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "move_issue"), Description("Move uma issue para outro status e/ou ordenação.")]
    public static async Task<CallToolResult> MoveIssueAsync(ITaskboardApiClient client, string issue_id, string status, double? sort_order = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var id = await ResolveIssueIdAsync(client, issue_id, cancellationToken);
            var request = new MoveTaskRequest(status, sort_order);
            var result = await client.PostAsync($"/api/tasks/{id}/move", request, cancellationToken);
            return Json(result?["task"] ?? new JsonObject());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "archive_issue"), Description("Arquiva uma issue.")]
    public static async Task<CallToolResult> ArchiveIssueAsync(ITaskboardApiClient client, string issue_id, CancellationToken cancellationToken = default)
    {
        try
        {
            var id = await ResolveIssueIdAsync(client, issue_id, cancellationToken);
            var result = await client.PostAsync($"/api/tasks/{id}/archive", null, cancellationToken);
            return Json(result?["task"] ?? new JsonObject());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "restore_issue"), Description("Restaura uma issue arquivada.")]
    public static async Task<CallToolResult> RestoreIssueAsync(ITaskboardApiClient client, string issue_id, CancellationToken cancellationToken = default)
    {
        try
        {
            var id = await ResolveIssueIdAsync(client, issue_id, cancellationToken);
            var result = await client.PostAsync($"/api/tasks/{id}/restore", null, cancellationToken);
            return Json(result?["task"] ?? new JsonObject());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "add_comment"), Description("Adiciona um comentário a uma issue.")]
    public static async Task<CallToolResult> AddCommentAsync(ITaskboardApiClient client, string issue_id, string body, CancellationToken cancellationToken = default)
    {
        try
        {
            var id = await ResolveIssueIdAsync(client, issue_id, cancellationToken);
            if (string.IsNullOrWhiteSpace(body))
            {
                return Error("O campo 'body' é obrigatório.");
            }

            var request = new CreateCommentRequest(body);
            var result = await client.PostAsync($"/api/tasks/{id}/comments", request, cancellationToken);
            return Json(result?["comment"] ?? new JsonObject());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "upload_attachment"), Description("Envia um arquivo local como anexo de uma issue.")]
    public static async Task<CallToolResult> UploadAttachmentAsync(ITaskboardApiClient client, string issue_id, string file_path, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(file_path))
            {
                return Error($"Arquivo não encontrado: {file_path}");
            }

            var id = await ResolveIssueIdAsync(client, issue_id, cancellationToken);
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(id), "taskId");
            content.Add(new StringContent("attachment"), "kind");
            using var stream = File.OpenRead(file_path);
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, "file", Path.GetFileName(file_path));

            var result = await client.PostMultipartAsync("/api/attachments", content, cancellationToken);
            return Json(result?["attachment"] ?? new JsonObject());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

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

    private static async Task<string> ResolveIssueIdAsync(ITaskboardApiClient client, string identifier, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new InvalidOperationException("Identificador da issue não informado.");
        }

        if (Guid.TryParse(identifier, out _))
        {
            return identifier;
        }

        var all = await client.GetAsync("/api/tasks", cancellationToken);
        var tasks = all?["tasks"] as JsonArray;
        if (tasks is null)
        {
            throw new InvalidOperationException($"Issue '{identifier}' não encontrada.");
        }

        foreach (var task in tasks)
        {
            if (task?["identifier"]?.GetValue<string>() == identifier || task?["id"]?.GetValue<string>() == identifier)
            {
                var id = task["id"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    return id;
                }
            }
        }

        throw new InvalidOperationException($"Issue '{identifier}' não encontrada.");
    }
}
