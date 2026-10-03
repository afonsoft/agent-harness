using Taskboard.Domain.Shared.Http;
using System.Text.Json.Nodes;

namespace Taskboard.Mcp.Services;

public interface ITaskboardApiClient
{
    Task<JsonNode?> GetAsync(string path, CancellationToken ct = default);
    Task<JsonNode?> PostAsync(string path, object? payload, CancellationToken ct = default);
    Task<JsonNode?> PutAsync(string path, object? payload, CancellationToken ct = default);
    Task<JsonNode?> PatchAsync(string path, object? payload, CancellationToken ct = default);
    Task DeleteAsync(string path, CancellationToken ct = default);
    Task<JsonNode?> PostMultipartAsync(string path, MultipartFormDataContent content, CancellationToken ct = default);
}

/// <summary>
/// MCP flavour of the shared API client (SPEC-20261003-ops-hardening RF-002):
/// keeps the tool-facing contract — InvalidOperationException messages and an
/// empty <see cref="JsonObject"/> instead of null on empty responses.
/// </summary>
public sealed class McpTaskboardApiClient : Taskboard.Domain.Shared.Http.TaskboardApiClient, ITaskboardApiClient
{
    public McpTaskboardApiClient(string baseUrl, string? apiKey = null, HttpMessageHandler? handler = null)
        : base(baseUrl, apiKey, handler)
    {
    }

    public Task<JsonNode?> PostMultipartAsync(string path, MultipartFormDataContent content, CancellationToken ct = default) =>
        base.PostMultipartAsync(path, content, ct);

    protected override JsonNode? EmptyResult() => new JsonObject();

    protected override Exception MapError(TaskboardApiException error) => error.Kind switch
    {
        TaskboardApiErrorKind.HttpStatus => new InvalidOperationException(
            $"API retornou {error.StatusCode}: {error.ResponseBody}"),
        _ => new InvalidOperationException(error.Message, error.InnerException),
    };
}
