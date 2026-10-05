using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// OpenAI wire message (chat completions) — role/content/tool_calls/
/// tool_call_id. When <see cref="ImageUrls"/> is present the wire content
/// becomes a parts array <c>[{text}, {image_url}, …]</c>
/// (SPEC-20261005-chat-attachments-feedback — attach:// images reach the
/// model through read_image results and user-message attachment parts).
/// </summary>
public sealed record OpenAiChatMessage(
    string Role,
    string? Content = null,
    IReadOnlyList<OpenAiToolCall>? ToolCalls = null,
    string? ToolCallId = null,
    string? Name = null,
    /// <summary>
    /// Data-URL/URL images appended as <c>image_url</c> parts after the text
    /// part. Only set when the model is expected to accept content parts.
    /// </summary>
    IReadOnlyList<string>? ImageUrls = null);

public sealed record OpenAiToolCall(string Id, string Name, string ArgumentsJson);

public sealed record OpenAiToolDefinition(string Name, string Description, string ParametersJson);

/// <summary>One parsed SSE chunk of a streaming chat completion.</summary>
/// <param name="ReasoningDelta">Reasoning/thinking delta (DeepSeek R1,
/// o-series, GLM) emitted in <c>delta.reasoning_content</c> — never part of
/// the persisted reply.</param>
public sealed record OpenAiStreamEvent(
    string? ContentDelta,
    IReadOnlyList<OpenAiToolCallDelta>? ToolCallDeltas,
    OpenAiUsage? Usage,
    string? FinishReason,
    string? ReasoningDelta = null);

public sealed record OpenAiToolCallDelta(int Index, string? Id, string? Name, string? ArgumentsDelta);

public sealed record OpenAiUsage(int? PromptTokens, int? CompletionTokens);

/// <summary>
/// Minimal OpenAI-compatible HTTP client (chat completions with streaming +
/// function calling, model listing, image generation) —
/// SPEC-20260929-ai-code-provider-chat RF-002/RF-005/RF-009. Works with any
/// `/v1` endpoint (OpenAI, Ollama, LM Studio, vLLM, OpenRouter, Groq).
/// The API key is sent as a Bearer header only — never logged.
/// </summary>
public sealed class OpenAiCompatibleClient(HttpClient http)
{
    private const string ToolTypeFunction = "function";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<IReadOnlyList<string>> ListModelsAsync(
        string baseUrl, string apiKey, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Normalize(baseUrl)}/models");
        AddAuth(request, apiKey);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ChatProviderException(
                $"Provider returned {(int)response.StatusCode} for /v1/models.",
                (int)response.StatusCode);
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var models = new List<string>();
        if (body.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray()
                .Where(item => item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String))
            {
                models.Add(item.GetProperty("id").GetString()!);
            }
        }

        return models.OrderBy(m => m, StringComparer.Ordinal).ToList();
    }

    /// <summary>Streams a chat completion; the caller accumulates tool-call deltas by index.</summary>
    /// <param name="maxTokens">Output budget — reasoning models (DeepSeek R1)
    /// spend tokens on <c>reasoning_content</c> before answering; a low cap
    /// ends the turn with empty content.</param>
    public async IAsyncEnumerable<OpenAiStreamEvent> StreamChatAsync(
        string baseUrl,
        string apiKey,
        string model,
        IReadOnlyList<OpenAiChatMessage> messages,
        IReadOnlyList<OpenAiToolDefinition>? tools,
        int? maxTokens = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages.Select(ToWire).ToList(),
            ["stream"] = true,
        };
        if (maxTokens is > 0)
        {
            payload["max_tokens"] = maxTokens.Value;
        }
        if (tools is { Count: > 0 })
        {
            payload["tools"] = tools.Select(t => new Dictionary<string, object?>
            {
                ["type"] = ToolTypeFunction,
                [ToolTypeFunction] = new Dictionary<string, object?>
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = JsonSerializer.Deserialize<JsonElement>(t.ParametersJson),
                },
            }).ToList();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Normalize(baseUrl)}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json"),
        };
        AddAuth(request, apiKey);
        request.Headers.Accept.ParseAdd("text/event-stream");

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new ChatProviderException(
                $"Provider returned {(int)response.StatusCode} for /v1/chat/completions: {Truncate(detail)}",
                (int)response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line["data:".Length..].Trim();
            if (data is "[DONE]" or "")
            {
                yield return new OpenAiStreamEvent(null, null, null, "stop");
                yield break;
            }

            OpenAiStreamEvent? parsed;
            try
            {
                parsed = ParseChunk(JsonSerializer.Deserialize<JsonElement>(data));
            }
            catch (JsonException)
            {
                parsed = null;
            }

            if (parsed is not null)
            {
                yield return parsed;
            }
        }
    }

    /// <summary>Generates one image via <c>/v1/images/generations</c>; returns base64 payload (RF-009).</summary>
    public async Task<string> GenerateImageAsync(
        string baseUrl, string apiKey, string model, string prompt, string? size, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["prompt"] = prompt,
            ["n"] = 1,
            ["response_format"] = "b64_json",
        };
        if (!string.IsNullOrWhiteSpace(size))
        {
            payload["size"] = size;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Normalize(baseUrl)}/images/generations")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json"),
        };
        AddAuth(request, apiKey);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new ChatProviderException(
                $"Provider returned {(int)response.StatusCode} for /v1/images/generations: {Truncate(detail)}",
                (int)response.StatusCode);
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var image = await ExtractImagePayloadAsync(body, cancellationToken).ConfigureAwait(false)
            ?? throw new ChatProviderException("Provider image response contained no image payload.", 502);
        return image;
    }

    private async Task<string?> ExtractImagePayloadAsync(JsonElement body, CancellationToken cancellationToken)
    {
        if (!body.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in data.EnumerateArray())
        {
            if (item.TryGetProperty("b64_json", out var b64) && b64.ValueKind == JsonValueKind.String)
            {
                return b64.GetString()!;
            }

            if (item.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String
                && url.GetString() is { } imageUrl
                && await TryDownloadImageAsync(imageUrl, cancellationToken).ConfigureAwait(false) is { } image)
            {
                return image;
            }
        }

        return null;
    }

    // Remote URL → download now so the image persists locally (RF-009).
    private async Task<string?> TryDownloadImageAsync(string url, CancellationToken cancellationToken)
    {
        using var imgRequest = new HttpRequestMessage(HttpMethod.Get, url);
        using var imgResponse = await http.SendAsync(imgRequest, cancellationToken).ConfigureAwait(false);
        if (!imgResponse.IsSuccessStatusCode)
        {
            return null;
        }

        var bytes = await imgResponse.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToBase64String(bytes);
    }

    private static object ToWire(OpenAiChatMessage message)
    {
        var wire = new Dictionary<string, object?>();
        if (message.ToolCallId is not null)
        {
            wire["tool_call_id"] = message.ToolCallId;
        }

        if (message.Name is not null)
        {
            wire["name"] = message.Name;
        }

        wire["role"] = message.Role;
        wire["content"] = message.ImageUrls is { Count: > 0 } images
            ? new List<Dictionary<string, object?>>
            {
                new() { ["type"] = "text", ["text"] = message.Content ?? string.Empty },
            }.Concat(images.Select(url => new Dictionary<string, object?>
            {
                ["type"] = "image_url",
                ["image_url"] = new Dictionary<string, object?> { ["url"] = url },
            })).ToList()
            : message.Content;
        if (message.ToolCalls is { Count: > 0 })
        {
            wire["tool_calls"] = message.ToolCalls.Select(tc => new Dictionary<string, object?>
            {
                ["id"] = tc.Id,
                ["type"] = ToolTypeFunction,
                [ToolTypeFunction] = new Dictionary<string, object?>
                {
                    ["name"] = tc.Name,
                    ["arguments"] = tc.ArgumentsJson,
                },
            }).ToList();
        }

        return wire;
    }

    private static OpenAiStreamEvent? ParseChunk(JsonElement chunk)
    {
        var usage = ParseUsage(chunk);
        var (content, toolDeltas, finish, reasoning) = ParseChoices(chunk);

        if (content is null && toolDeltas is null && usage is null && finish is null && reasoning is null)
        {
            return null;
        }

        return new OpenAiStreamEvent(content, toolDeltas, usage, finish, reasoning);
    }

    private static OpenAiUsage? ParseUsage(JsonElement chunk)
    {
        if (!chunk.TryGetProperty("usage", out var usageEl) || usageEl.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new OpenAiUsage(
            usageEl.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var pi) ? pi : null,
            usageEl.TryGetProperty("completion_tokens", out var c) && c.TryGetInt32(out var cv) ? cv : null);
    }

    private static (string? Content, List<OpenAiToolCallDelta>? ToolDeltas, string? Finish, string? Reasoning) ParseChoices(JsonElement chunk)
    {
        if (!chunk.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
        {
            return (null, null, null, null);
        }

        string? content = null;
        List<OpenAiToolCallDelta>? toolDeltas = null;
        string? finish = null;
        string? reasoning = null;
        foreach (var choice in choices.EnumerateArray())
        {
            if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
            {
                finish = fr.GetString();
            }

            if (!choice.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var (c, r) = ParseDelta(delta, ref toolDeltas);
            content = c ?? content;
            reasoning = r ?? reasoning;
        }

        return (content, toolDeltas, finish, reasoning);
    }

    private static (string? Content, string? Reasoning) ParseDelta(
        JsonElement delta, ref List<OpenAiToolCallDelta>? toolDeltas)
    {
        string? content = null;
        string? reasoning = null;

        if (delta.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String
            && contentEl.GetString() is { Length: > 0 } text)
        {
            content = text;
        }

        if (delta.TryGetProperty("reasoning_content", out var reasoningEl) && reasoningEl.ValueKind == JsonValueKind.String
            && reasoningEl.GetString() is { Length: > 0 } reasoningText)
        {
            reasoning = reasoningText;
        }

        if (delta.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
        {
            toolDeltas = ParseToolCallDeltas(tcs, toolDeltas);
        }

        return (content, reasoning);
    }

    private static List<OpenAiToolCallDelta> ParseToolCallDeltas(
        JsonElement toolCalls, List<OpenAiToolCallDelta>? existing)
    {
        var deltas = existing ?? [];
        foreach (var tc in toolCalls.EnumerateArray())
        {
            deltas.Add(ParseToolCallDelta(tc));
        }

        return deltas;
    }

    private static OpenAiToolCallDelta ParseToolCallDelta(JsonElement tc)
    {
        var index = tc.TryGetProperty("index", out var idx) && idx.TryGetInt32(out var i) ? i : 0;
        var id = tc.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : null;
        string? name = null;
        string? args = null;
        if (tc.TryGetProperty(ToolTypeFunction, out var fn) && fn.ValueKind == JsonValueKind.Object)
        {
            name = fn.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            args = fn.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
        }

        return new OpenAiToolCallDelta(index, id, name, args);
    }

    private static void AddAuth(HttpRequestMessage request, string apiKey)
    {
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    private static string Normalize(string baseUrl) =>
        baseUrl.TrimEnd('/').EndsWith("/v1", StringComparison.Ordinal)
            ? baseUrl.TrimEnd('/')
            : $"{baseUrl.TrimEnd('/')}/v1";

    private static string Truncate(string value) =>
        value.Length <= 400 ? value.ReplaceLineEndings(" ") : $"{value[..400]}…";
}

/// <summary>Provider-side failure (HTTP status / bad payload) surfaced as 502 to the client (RF-005).</summary>
public sealed class ChatProviderException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
