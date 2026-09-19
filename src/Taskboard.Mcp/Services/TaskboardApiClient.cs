using System.Net.Http.Json;
using System.Text.Json;
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

public sealed class TaskboardApiClient : ITaskboardApiClient
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public TaskboardApiClient(string baseUrl, string? apiKey = null)
    {
        _client = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/')) };

        // SPEC-20260915-api-authorization-hardening RF-004: machine clients
        // authenticate via X-Api-Key when the key is configured. An explicit
        // key (e.g. resolved from IConfiguration by the in-process host) wins
        // over the TASKBOARD_API_KEY environment variable.
        apiKey ??= Environment.GetEnvironmentVariable("TASKBOARD_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _client.DefaultRequestHeaders.Add("X-Api-Key", apiKey.Trim());
        }
    }

    public async Task<JsonNode?> GetAsync(string path, CancellationToken ct = default)
    {
        var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, path), ct);
        return await ReadJsonAsync(response, ct);
    }

    public async Task<JsonNode?> PostAsync(string path, object? payload, CancellationToken ct = default)
    {
        var response = await SendAsync(() => CreateJsonRequest(HttpMethod.Post, path, payload), ct);
        return await ReadJsonAsync(response, ct);
    }

    public async Task<JsonNode?> PutAsync(string path, object? payload, CancellationToken ct = default)
    {
        var response = await SendAsync(() => CreateJsonRequest(HttpMethod.Put, path, payload), ct);
        return await ReadJsonAsync(response, ct);
    }

    public async Task<JsonNode?> PatchAsync(string path, object? payload, CancellationToken ct = default)
    {
        var response = await SendAsync(() => CreateJsonRequest(HttpMethod.Patch, path, payload), ct);
        return await ReadJsonAsync(response, ct);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, path), ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<JsonNode?> PostMultipartAsync(string path, MultipartFormDataContent content, CancellationToken ct = default)
    {
        var response = await _client.PostAsync(path, content, ct);
        await EnsureSuccessAsync(response);
        return await ReadJsonAsync(response, ct);
    }

    private HttpRequestMessage CreateJsonRequest(HttpMethod method, string path, object? payload)
    {
        var request = new HttpRequestMessage(method, path);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: _options);
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> factory, CancellationToken ct)
    {
        try
        {
            var response = await _client.SendAsync(factory(), HttpCompletionOption.ResponseContentRead, ct);
            await EnsureSuccessAsync(response);
            return response;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Falha na comunicação com a API Taskboard: {ex.Message}", ex);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException($"API retornou {(int)response.StatusCode}: {body}");
    }

    private static async Task<JsonNode?> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var content = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(content))
        {
            return new JsonObject();
        }

        return JsonNode.Parse(content);
    }
}
