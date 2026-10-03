using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Taskboard.Domain.Shared.Configuration;

namespace Taskboard.Domain.Shared.Http;

/// <summary>Kind of failure surfaced by <see cref="TaskboardApiException"/>.</summary>
public enum TaskboardApiErrorKind
{
    /// <summary>The server answered with a non-success HTTP status.</summary>
    HttpStatus,

    /// <summary>The request never got an HTTP response (DNS, refused, socket errors).</summary>
    Transport,

    /// <summary>The HTTP client timed out waiting for a response.</summary>
    Timeout,
}

/// <summary>
/// Failure raised by <see cref="TaskboardApiClient"/>. Carries everything a
/// consumer needs to map it onto its own error contract (CLI exit codes, MCP
/// tool errors, …) via <see cref="TaskboardApiClient.MapError"/>.
/// </summary>
public class TaskboardApiException : InvalidOperationException
{
    public TaskboardApiErrorKind Kind { get; }
    public int? StatusCode { get; }
    public string? ResponseBody { get; }

    /// <summary>The server's <c>error.message</c> (or <c>message</c>) when the body is JSON.</summary>
    public string? ServerMessage { get; }
    public Uri? BaseAddress { get; }

    public TaskboardApiException(
        TaskboardApiErrorKind kind,
        int? statusCode,
        string? responseBody,
        string? serverMessage,
        Uri? baseAddress,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        StatusCode = statusCode;
        ResponseBody = responseBody;
        ServerMessage = serverMessage;
        BaseAddress = baseAddress;
    }
}

/// <summary>
/// Shared HTTP client for the Taskboard API used by both taskctl (Taskboard.Cli)
/// and the MCP server (Taskboard.Mcp). SPEC-20261003-ops-hardening RF-002: one
/// transport implementation; each consumer subclasses only to translate errors
/// (<see cref="MapError"/>) and the empty-body result (<see cref="EmptyResult"/>).
/// </summary>
public class TaskboardApiClient
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public TaskboardApiClient(string baseUrl, string? apiKey = null, HttpMessageHandler? handler = null)
    {
        _client = new HttpClient(handler ?? new HttpClientHandler())
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/')),
        };
        _client.DefaultRequestHeaders.Add("Accept", "application/json");

        // SPEC-20260915-api-authorization-hardening RF-004: machine clients
        // authenticate via X-Api-Key when the key is configured. An explicit
        // key (e.g. resolved from IConfiguration by an in-process host) wins
        // over the HARNESS_API_KEY environment variable.
        apiKey ??= HarnessEnv.Get("HARNESS_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _client.DefaultRequestHeaders.Add("X-Api-Key", apiKey.Trim());
        }
    }

    protected Uri? BaseAddress => _client.BaseAddress;

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
        await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, path), ct);
    }

    public async Task<JsonNode?> PostMultipartAsync(string path, HttpContent content, CancellationToken ct = default)
    {
        var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, path) { Content = content }, ct);
        return await ReadJsonAsync(response, ct);
    }

    public async Task DownloadAsync(string path, Stream destination, CancellationToken ct = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await _client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            throw MapError(TransportError(ex));
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw MapError(TimeoutError(ex));
        }

        using (response)
        {
            await EnsureSuccessAsync(response, ct);
            await response.Content.CopyToAsync(destination, ct);
        }
    }

    /// <summary>Result returned when the response body is empty. Cli returns null; Mcp returns an empty object.</summary>
    protected virtual JsonNode? EmptyResult() => null;

    /// <summary>Translate a transport failure into the consumer's own error contract.</summary>
    protected virtual Exception MapError(TaskboardApiException error) => error;

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
            var response = await _client.SendAsync(factory(), ct);
            await EnsureSuccessAsync(response, ct);
            return response;
        }
        catch (TaskboardApiException ex)
        {
            throw MapError(ex);
        }
        catch (HttpRequestException ex)
        {
            throw MapError(TransportError(ex));
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw MapError(TimeoutError(ex));
        }
    }

    private TaskboardApiException TransportError(HttpRequestException ex) =>
        new(TaskboardApiErrorKind.Transport, null, null, null, _client.BaseAddress,
            $"Falha na comunicação com a API Taskboard: {ex.Message}", ex);

    private TaskboardApiException TimeoutError(TaskCanceledException ex) =>
        new(TaskboardApiErrorKind.Timeout, null, null, null, _client.BaseAddress,
            $"Timeout ao conectar em {_client.BaseAddress}: {ex.Message}", ex);

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        string? serverMessage = null;
        try
        {
            var json = JsonNode.Parse(body);
            serverMessage = json?["error"]?["message"]?.GetValue<string>() ?? json?["message"]?.GetValue<string>();
        }
        catch
        {
            // body isn't JSON — the raw body stays in ResponseBody
        }

        throw new TaskboardApiException(
            TaskboardApiErrorKind.HttpStatus,
            (int)response.StatusCode,
            body,
            serverMessage,
            _client.BaseAddress,
            $"API retornou {(int)response.StatusCode}: {body}");
    }

    private async Task<JsonNode?> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var content = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(content))
        {
            return EmptyResult();
        }

        return JsonNode.Parse(content);
    }
}
