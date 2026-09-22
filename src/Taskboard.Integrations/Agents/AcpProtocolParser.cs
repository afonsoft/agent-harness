using System.Text.Json;
using Taskboard.Agents;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Façade over the versioned ACP parsers (SPEC-20260921-acp-v2-readiness
/// RF-205): the JSON-RPC envelope (id/method/params) is version-agnostic, so
/// it is parsed here; <c>session/update</c> variants and
/// <c>session/request_permission</c> are delegated to the negotiated
/// <see cref="IAcpDialect"/>. <see cref="Parse(string)"/> keeps v1 semantics
/// for transports that never negotiate (one-shot JSON-RPC stdout).
/// </summary>
public static class AcpProtocolParser
{
    public enum MessageType
    {
        /// <summary>Agent → client, no id: session/update and notifications.</summary>
        Notification,

        /// <summary>Agent → client with id: requires a response (request_permission, fs/*, terminal/*).</summary>
        Request,

        /// <summary>Agent → client: response to one of our requests (initialize/session/new/prompt).</summary>
        Response
    }

    /// <param name="MessageId">v2 message correlation id (message/chunk upserts).</param>
    /// <param name="PlanId">v2 plan correlation id (plan_update).</param>
    /// <param name="PatchOp">Upsert merge hint — append (default/null), replace or clear.</param>
    /// <param name="IsToolCallUpsert">v2 tool_call_update: first-seen resolves to tool_call, patches to tool_output.</param>
    public sealed record Parsed(
        MessageType Type,
        string Method,
        string Kind,
        string? Content,
        string? PayloadJson,
        string? SessionId = null,
        string? ToolCallId = null,
        string? RequestId = null,
        JsonElement ResponseResult = default,
        JsonElement ResponseError = default,
        JsonElement Params = default,
        string? MessageId = null,
        string? PlanId = null,
        string? PatchOp = null,
        bool IsToolCallUpsert = false,
        string? Role = null);

    /// <summary>Returns null when the line is not JSON. Defaults to the v1 dialect.</summary>
    public static Parsed? Parse(string line) => Parse(line, AcpDialects.V1);

    /// <summary>Parses one JSON-RPC message using the negotiated <paramref name="dialect"/>.</summary>
    public static Parsed? Parse(string line, IAcpDialect dialect)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            return ParseElement(doc.RootElement, dialect);
        }
    }

    /// <summary>Parses an already-materialized JSON-RPC message (batch entry).</summary>
    public static Parsed? ParseElement(JsonElement root, IAcpDialect dialect)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var hasId = root.TryGetProperty("id", out var idEl)
            && idEl.ValueKind is JsonValueKind.String or JsonValueKind.Number;
        var method = root.TryGetProperty("method", out var m) ? m.GetString() ?? string.Empty : string.Empty;
        var requestId = hasId ? JsonElementToId(idEl) : null;

        // Response: {id, result|error}, sem method.
        if (hasId && string.IsNullOrEmpty(method))
        {
            var hasResult = root.TryGetProperty("result", out var result);
            var hasError = root.TryGetProperty("error", out var error);
            return new Parsed(MessageType.Response, string.Empty, "response", null, null,
                RequestId: requestId,
                ResponseResult: hasResult ? result.Clone() : default,
                ResponseError: hasError ? error.Clone() : default);
        }

        var isRequest = hasId && !string.IsNullOrEmpty(method);
        var type = isRequest ? MessageType.Request : MessageType.Notification;

        if (!root.TryGetProperty("params", out var p) || p.ValueKind != JsonValueKind.Object)
        {
            return new Parsed(type, method, "message", null, null, RequestId: requestId,
                Params: root.TryGetProperty("params", out var raw) ? raw.Clone() : default);
        }

        var parsed = method switch
        {
            "session/update" => dialect.ParseSessionUpdate(p, requestId),
            "session/request_permission" => dialect.ParsePermission(p, requestId, isRequest),
            _ when p.TryGetProperty("kind", out var legacyKind) => new Parsed(
                type, method, legacyKind.GetString() ?? "message",
                p.TryGetProperty("content", out var lc) ? lc.GetString() : null,
                p.GetRawText(), RequestId: requestId),
            _ => new Parsed(type, method, "activity", null, p.GetRawText(), RequestId: requestId)
        };
        return parsed with { Params = p.Clone() };
    }

    /// <summary>
    /// Extracts display text from an ACP content field — plain string, a single
    /// content block (<c>{type:"text", text}</c>) or a v2 content-block array.
    /// Non-text blocks (diff/image/resource) stay in the raw payload.
    /// </summary>
    internal static string? ExtractText(JsonElement update)
    {
        if (!update.TryGetProperty("content", out var content))
        {
            return null;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        // ACP content block: { type: "text", text: "..." }
        if (content.ValueKind == JsonValueKind.Object
            && content.TryGetProperty("text", out var text))
        {
            return text.GetString();
        }

        // v2: content is an array of blocks — concatenate the text ones.
        if (content.ValueKind == JsonValueKind.Array)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind == JsonValueKind.Object
                    && block.TryGetProperty("text", out var bt)
                    && bt.ValueKind == JsonValueKind.String)
                {
                    sb.Append(bt.GetString());
                }
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }

        return content.GetRawText();
    }

    private static string JsonElementToId(JsonElement id) => id.ValueKind switch
    {
        JsonValueKind.String => id.GetString() ?? string.Empty,
        JsonValueKind.Number => id.GetRawText(),
        _ => string.Empty
    };
}
