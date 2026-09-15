using System.Net.Http.Json;
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
    public TaskboardClient(HttpClient httpClient, CircuitAuthContext authContext)
    {
        _httpClient = httpClient;
        if (!string.IsNullOrEmpty(authContext.Cookie))
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", authContext.Cookie);
        }
    }

    /// <summary>
    /// Retorna todos os projetos cadastrados.
    /// </summary>
    public async Task<IReadOnlyCollection<ProjectDto>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<ProjectListResponse>("/api/projects", cancellationToken);
        return response?.Projects ?? [];
    }

    /// <summary>
    /// Retorna as tarefas do projeto especificado.
    /// </summary>
    public async Task<IReadOnlyCollection<TaskDto>> GetTasksAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<TaskListDto>($"/api/tasks?projectId={projectId}", cancellationToken);
        return response?.Tasks ?? [];
    }

    /// <summary>
    /// Retorna os comentários da tarefa especificada.
    /// </summary>
    public async Task<IReadOnlyList<CommentDto>> GetTaskCommentsAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<CommentListResponse>($"/api/tasks/{Uri.EscapeDataString(taskId)}/comments", cancellationToken);
        return response?.Comments ?? [];
    }

    /// <summary>
    /// Adiciona um comentário à tarefa especificada.
    /// </summary>
    public async Task<CommentDto?> AddCommentAsync(string taskId, string body, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync($"/api/tasks/{Uri.EscapeDataString(taskId)}/comments", new CreateCommentRequest(body), cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CommentResponse>(cancellationToken);
        return result?.Comment;
    }

    /// <summary>
    /// Retorna os anexos da tarefa especificada.
    /// </summary>
    public async Task<IReadOnlyList<AttachmentDto>> GetTaskAttachmentsAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<AttachmentListResponse>($"/api/tasks/{Uri.EscapeDataString(taskId)}/attachments", cancellationToken);
        return response?.Attachments ?? [];
    }

    /// <summary>
    /// Faz upload de um anexo para a tarefa especificada.
    /// </summary>
    public async Task<AttachmentDto?> UploadAttachmentAsync(string taskId, Stream content, string filename, string contentType, CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(content);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", filename);
        form.Add(new StringContent(taskId), "taskId");

        var response = await _httpClient.PostAsync("/api/attachments", form, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AttachmentResponse>(cancellationToken);
        return result?.Attachment;
    }

    /// <summary>
    /// Remove o anexo especificado.
    /// </summary>
    public async Task DeleteAttachmentAsync(string attachmentId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/api/attachments/{Uri.EscapeDataString(attachmentId)}", cancellationToken);
        response.EnsureSuccessStatusCode();
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

    private sealed record ProjectListResponse(List<ProjectDto> Projects);
    private sealed record CommentListResponse(List<CommentDto> Comments);
    private sealed record CommentResponse(CommentDto Comment);
    private sealed record AttachmentListResponse(List<AttachmentDto> Attachments);
    private sealed record AttachmentResponse(AttachmentDto Attachment);
    private sealed record WorkflowWorkspaceListResponse(List<WorkflowWorkspaceDto> Workspaces);
    private sealed record AiChatThreadListResponse(List<AiChatThreadDto> Threads);
    private sealed record SettingsResponse(SettingsDto Settings);
    private sealed record SkillsResponse(List<SkillDto> Skills);
    private sealed record SkillDetailResponse(SkillDetailDto Skill);
    private sealed record SkillFileContentResponse(string Path, string Content);
}
