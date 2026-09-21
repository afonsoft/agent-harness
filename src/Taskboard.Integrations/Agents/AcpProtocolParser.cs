using System.Text.Json;
using Taskboard.Agents;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Parser conforme ao protocolo ACP real (SPEC-20260921-agent-execution-event-pipeline
/// RF-002): mensagens JSON-RPC de agente — notificações <c>session/update</c> com
/// discriminador <c>update.sessionUpdate</c>, requests <c>session/request_permission</c>
/// (com <c>id</c> para reply) e responses a requests do cliente.
/// Tolera o shape legado (<c>params.kind</c>/<c>params.content</c>) para CLIs que
/// ainda o emitem.
/// </summary>
public static class AcpProtocolParser
{
    public enum MessageType
    {
        /// <summary>Agente → cliente, sem id: session/update e notificações.</summary>
        Notification,

        /// <summary>Agente → cliente com id: exige resposta (request_permission, fs/*, terminal/*).</summary>
        Request,

        /// <summary>Agente → cliente: resposta a um request nosso (initialize/session/new/prompt).</summary>
        Response
    }

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
        JsonElement ResponseError = default);

    /// <summary>Retorna null quando a linha não é JSON.</summary>
    public static Parsed? Parse(string line)
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
            var root = doc.RootElement;
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
                return new Parsed(type, method, "message", null, null, RequestId: requestId);
            }

            return method switch
            {
                "session/update" => ParseSessionUpdate(p, requestId),
                "session/request_permission" => ParsePermission(p, requestId, isRequest),
                _ when p.TryGetProperty("kind", out var legacyKind) => new Parsed(
                    type, method, legacyKind.GetString() ?? "message",
                    p.TryGetProperty("content", out var lc) ? lc.GetString() : null,
                    p.GetRawText(), RequestId: requestId),
                _ => new Parsed(type, method, "activity", null, p.GetRawText(), RequestId: requestId)
            };
        }
    }

    private static Parsed ParseSessionUpdate(JsonElement p, string? requestId)
    {
        var sessionId = p.TryGetProperty("sessionId", out var sid) ? sid.GetString() : null;

        if (!p.TryGetProperty("update", out var update) || update.ValueKind != JsonValueKind.Object)
        {
            // Shape legado: session/update com params.kind/content direto.
            if (p.TryGetProperty("kind", out var legacyKind))
            {
                return new Parsed(
                    MessageType.Notification, "session/update",
                    legacyKind.GetString() ?? "message",
                    p.TryGetProperty("content", out var lc) ? lc.GetString() : null,
                    p.GetRawText(), sessionId, RequestId: requestId);
            }

            return new Parsed(MessageType.Notification, "session/update", "activity", null,
                p.GetRawText(), SessionId: sessionId, RequestId: requestId);
        }

        var updateKind = update.TryGetProperty("sessionUpdate", out var su)
            ? su.GetString() ?? string.Empty
            : string.Empty;

        var toolCallId = update.TryGetProperty("toolCallId", out var tcid) ? tcid.GetString() : null;
        var payload = update.GetRawText();

        return updateKind switch
        {
            "agent_message_chunk" => new Parsed(
                MessageType.Notification, "session/update", AgentEventKinds.Message,
                ExtractText(update), payload, sessionId, RequestId: requestId),
            "agent_thought_chunk" => new Parsed(
                MessageType.Notification, "session/update", AgentEventKinds.Thought,
                ExtractText(update), payload, sessionId, RequestId: requestId),
            "tool_call" => new Parsed(
                MessageType.Notification, "session/update", AgentEventKinds.ToolCall,
                update.TryGetProperty("title", out var t) ? t.GetString() : null,
                payload, sessionId, toolCallId, requestId),
            "tool_call_update" => new Parsed(
                MessageType.Notification, "session/update", AgentEventKinds.ToolOutput,
                null, payload, sessionId, toolCallId, requestId),
            "plan" => new Parsed(
                MessageType.Notification, "session/update", AgentEventKinds.Plan,
                null, payload, sessionId, RequestId: requestId),
            "usage_update" => new Parsed(
                MessageType.Notification, "session/update", AgentEventKinds.Metric,
                null, payload, sessionId, RequestId: requestId),
            _ => new Parsed(
                MessageType.Notification, "session/update", AgentEventKinds.Activity,
                ExtractText(update), payload, sessionId, RequestId: requestId)
        };
    }

    private static Parsed ParsePermission(JsonElement p, string? requestId, bool isRequest)
    {
        // Shape real ACP: params { sessionId, toolCall: {...}, options: [{optionId, name, kind}] }
        // Shape legado: params { requestId, tool, detail, options: ["allow","deny"] }
        string? sessionId = p.TryGetProperty("sessionId", out var sid) ? sid.GetString() : null;
        string tool = string.Empty;
        string detail = string.Empty;
        var options = new List<string>();

        if (p.TryGetProperty("toolCall", out var toolCall) && toolCall.ValueKind == JsonValueKind.Object)
        {
            tool = toolCall.TryGetProperty("title", out var tt) ? tt.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrEmpty(tool))
            {
                tool = toolCall.TryGetProperty("kind", out var tk) ? tk.GetString() ?? string.Empty : string.Empty;
            }
            detail = toolCall.TryGetProperty("rawInput", out var ri) ? ri.GetRawText() : tool;
        }
        else
        {
            tool = p.TryGetProperty("tool", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            detail = p.TryGetProperty("detail", out var d) ? d.GetString() ?? string.Empty : string.Empty;
        }

        if (p.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array)
        {
            foreach (var opt in opts.EnumerateArray())
            {
                if (opt.ValueKind == JsonValueKind.Object
                    && opt.TryGetProperty("optionId", out var oid)
                    && oid.GetString() is { } optionId)
                {
                    options.Add(optionId);
                }
                else if (opt.ValueKind == JsonValueKind.String && opt.GetString() is { } legacy)
                {
                    options.Add(legacy);
                }
            }
        }

        if (options.Count == 0)
        {
            options.AddRange(["allow", "deny"]);
        }

        // No ACP real o id da request JSON-RPC é a correlação do reply;
        // no shape legado, params.requestId.
        var effectiveRequestId = isRequest
            ? requestId ?? Guid.NewGuid().ToString("N")
            : p.TryGetProperty("requestId", out var rid) ? rid.GetString() ?? string.Empty : string.Empty;

        var payload = JsonSerializer.Serialize(new
        {
            requestId = effectiveRequestId,
            tool,
            detail,
            options
        });

        return new Parsed(
            isRequest ? MessageType.Request : MessageType.Notification,
            "session/request_permission",
            AgentEventKinds.Permission,
            detail,
            payload,
            sessionId,
            RequestId: requestId);
    }

    private static string? ExtractText(JsonElement update)
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

        return content.GetRawText();
    }

    private static string JsonElementToId(JsonElement id) => id.ValueKind switch
    {
        JsonValueKind.String => id.GetString() ?? string.Empty,
        JsonValueKind.Number => id.GetRawText(),
        _ => string.Empty
    };
}
