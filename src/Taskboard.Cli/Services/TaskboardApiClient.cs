using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Taskboard.Cli.Services;

public sealed class TaskboardApiClient
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _options;

    public TaskboardApiClient(string baseUrl)
    {
        _client = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/')) };
        _client.DefaultRequestHeaders.Add("Accept", "application/json");

        // SPEC-20260915-api-authorization-hardening RF-004: machine clients
        // authenticate via X-Api-Key when the key is configured.
        var apiKey = Environment.GetEnvironmentVariable("TASKBOARD_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _client.DefaultRequestHeaders.Add("X-Api-Key", apiKey.Trim());
        }

        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
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
        if (!response.IsSuccessStatusCode)
        {
            await ThrowAsync(response, ct);
        }
    }


    public async Task DownloadAsync(string path, Stream destination, CancellationToken ct = default)
    {
        using var response = await _client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowAsync(response, ct);
        }

        await response.Content.CopyToAsync(destination, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> create, CancellationToken ct)
    {
        try
        {
            var response = await _client.SendAsync(create(), ct);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowAsync(response, ct);
            }

            return response;
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException || ex.InnerException is IOException)
        {
            throw new CliException(3, $"Servidor indisponível em {_client.BaseAddress}: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
        {
            throw new CliException(3, $"Timeout ao conectar em {_client.BaseAddress}: {ex.Message}");
        }
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

    private static async Task<JsonNode?> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var content = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        return JsonNode.Parse(content);
    }

    private static async Task ThrowAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        var code = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => 4,
            HttpStatusCode.Conflict => 5,
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => 2,
            _ => 1,
        };

        string? message = null;
        try
        {
            var json = JsonNode.Parse(body);
            message = json?["error"]?["message"]?.GetValue<string>() ?? json?["message"]?.GetValue<string>();
        }
        catch
        {
            // ignore
        }

        throw new CliException(code, message ?? $"Erro {(int)response.StatusCode}: {body}");
    }
}

public sealed class CliException : Exception
{
    public int ExitCode { get; }

    public CliException(int exitCode, string message)
        : base(message)
    {
        ExitCode = exitCode;
    }
}
