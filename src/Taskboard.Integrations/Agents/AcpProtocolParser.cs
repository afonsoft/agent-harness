using System.Text.Json;
using Taskboard.Agents;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Parser conforme ao protocolo ACP real (SPEC-20260921-agent-execution-event-pipeline
/// RF-002): agent JSON-RPC messages — <c>session/update</c> notifications with
/// the <c>update.sessionUpdate</c> discriminant, <c>session/request_permission</c>
/// requests (with <c>id</c> for replies) and responses to client requests.
/// Tolerates the legacy shape (<c>params.kind</c>/<c>params.content</c>) for CLIs that
/// ainda o emitem.
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
        // SPEC-20260921-acp-v2-readiness RF-203: upsert-capable envelope fields,
        // populated by the v2 parser; always null under v1.
        string? MessageId = null,
        string? PlanId = null,
        string? PatchOp = null,
        string? EntityKind = null);

    /// <summary>Returns null when the line is not JSON.</summary>
    public static Parsed? Parse(string line, int protocolVersion = 1)
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
                return new Parsed(type, method, "message", null, null, RequestId: requestId,
                    Params: root.TryGetProperty("params", out var raw) ? raw.Clone() : default);
            }

            var parsed = method switch
            {
                "session/update" when protocolVersion >= 2 => ParseSessionUpdateV2(p, requestId),
                "session/update" => ParseSessionUpdate(p, requestId),
                "session/request_permission" when protocolVersion >= 2 =>
                    ParsePermissionV2(p, requestId, isRequest),
                "session/request_permission" => ParsePermission(p, requestId, isRequest),
                _ when p.TryGetProperty("kind", out var legacyKind) => new Parsed(
                    type, method, legacyKind.GetString() ?? "message",
                    p.TryGetProperty("content", out var lc) ? lc.GetString() : null,
                    p.GetRawText(), RequestId: requestId),
                _ => new Parsed(type, method, "activity", null, p.GetRawText(), RequestId: requestId)
            };
            return parsed with { Params = p.Clone() };
        }
    }

    private static Parsed ParseSessionUpdate(JsonElement p, string? requestId)
    {
        var sessionId = p.TryGetProperty("sessionId", out var sid) ? sid.GetString() : null;

        if (!p.TryGetProperty("update", out var update) || update.ValueKind != JsonValueKind.Object)
        {
            // Legacy shape: session/update with direct params.kind/content.
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
            // RF-007: replayed user messages (session/load) and agent-advertised
            // slash commands / mode / config / session metadata all get their
            // own normalized kinds instead of falling into generic activity.
            "user_message_chunk" => new Parsed(
                MessageType.Notification, "session/update", AgentEventKinds.Message,
                ExtractText(update), payload, sessionId, RequestId: requestId),
            "available_commands_update" => new Parsed(
                MessageType.Notification, "session/update", AgentEventKinds.Commands,
                null, payload, sessionId, RequestId: requestId),
            "current_mode_update" or "config_option_update" or "session_info_update" => new Parsed(
                MessageType.Notification, "session/update", AgentEventKinds.SessionInfo,
                null, payload, sessionId, RequestId: requestId),
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

    /// <summary>
    /// SPEC-20260921-acp-v2-readiness RF-205/RF-206: v2 session/update variants.
    /// Upsert semantics — messages, tool calls and plans are patched by id
    /// (MessageId/ToolCallId/PlanId + PatchOp/EntityKind on the envelope).
    /// Unknown discriminants degrade to a generic activity event with the raw
    /// payload preserved (forward compatibility); known variants missing a
    /// required identity field surface as parse errors instead.
    /// </summary>
    private static Parsed ParseSessionUpdateV2(JsonElement p, string? requestId)
    {
        var sessionId = p.TryGetProperty("sessionId", out var sid) ? sid.GetString() : null;

        if (!p.TryGetProperty("update", out var update) || update.ValueKind != JsonValueKind.Object)
        {
            return new Parsed(MessageType.Notification, "session/update", "activity", null,
                p.GetRawText(), SessionId: sessionId, RequestId: requestId);
        }

        var updateKind = update.TryGetProperty("sessionUpdate", out var su)
            ? su.GetString() ?? string.Empty
            : string.Empty;
        var payload = update.GetRawText();

        var messageId = update.TryGetProperty("messageId", out var mid) ? mid.GetString() : null;
        var toolCallId = update.TryGetProperty("toolCallId", out var tcid) ? tcid.GetString() : null;
        var planId = update.TryGetProperty("planId", out var pid) ? pid.GetString() : null;

        Parsed Malformed(string variant, string field) => new(
            MessageType.Notification, "session/update", AgentEventKinds.Error,
            $"Malformed {variant}: missing required '{field}'.", payload, sessionId,
            RequestId: requestId);

        return updateKind switch
        {
            "agent_message_chunk" => messageId is null ? Malformed(updateKind, "messageId") : new Parsed(
                MessageType.Notification, "session/update", AgentEventKinds.Message,
                ExtractText(update), payload, sessionId, RequestId: requestId,
                MessageId: messageId, PatchOp: "append", EntityKind: "message"),
            "user_message_chunk" => messageId is null ? Malformed(updateKind, "messageId") : new Parsed(
                MessageType.Notification, "session/update", AgentEventKinds.Message,
                ExtractText(update), payload, sessionId, RequestId: requestId,
                MessageId: messageId, PatchOp: "append", EntityKind: "message"),
            "agent_thought_chunk" => messageId is null ? Malformed(updateKind, "messageId") : new Parsed(
                MessageType.Notification, "session/update", AgentEventKinds.Thought,
                ExtractText(update), payload, sessionId, RequestId: requestId,
                MessageId: messageId, PatchOp: "append", EntityKind: "message"),
            // Whole-message upserts — content is an array of blocks.
            "user_message" or "agent_message" => messageId is null ? Malformed(updateKind, "messageId") : new Parsed(
                MessageType.Notification, "session/update", AgentEventKinds.Message,
                ExtractText(update), payload, sessionId, RequestId: requestId,
                MessageId: messageId, PatchOp: "upsert", EntityKind: "message"),
            "agent_thought" => messageId is null ? Malformed(updateKind, "messageId") : new Parsed(
                MessageType.Notification, "session/update", AgentEventKinds.Thought,
                ExtractText(update), payload, sessionId, RequestId: requestId,
                MessageId: messageId, PatchOp: "upsert", EntityKind: "message"),
            // Foreground lifecycle — running/idle/requires_action + stopReason.
            "state_update" => !update.TryGetProperty("state", out _)
                ? Malformed(updateKind, "state")
                : new Parsed(MessageType.Notification, "session/update", AgentEventKinds.State,
                    null, payload, sessionId, RequestId: requestId),
            // The first tool_call_update for a toolCallId creates the entity;
            // the client reclassifies it as tool_call on first sight.
            "tool_call_update" => toolCallId is null ? Malformed(updateKind, "toolCallId") : new Parsed(
                MessageType.Notification, "session/update", AgentEventKinds.ToolOutput,
                update.TryGetProperty("title", out var t) ? t.GetString() : null,
                payload, sessionId, toolCallId, requestId,
                PatchOp: "upsert", EntityKind: "tool_call"),
            "tool_call_content_chunk" => toolCallId is null ? Malformed(updateKind, "toolCallId") : new Parsed(
                MessageType.Notification, "session/update", AgentEventKinds.ToolOutput,
                null, payload, sessionId, toolCallId, requestId,
                PatchOp: "append", EntityKind: "tool_call"),
            // Agent-owned display terminal — display only, never a client method.
            "terminal_update" or "terminal_output_chunk" => new Parsed(
                MessageType.Notification, "session/update", AgentEventKinds.Output,
                null, payload, sessionId, RequestId: requestId,
                PatchOp: updateKind == "terminal_output_chunk" ? "append" : "upsert",
                EntityKind: "terminal"),
            "plan_update" => planId is null ? Malformed(updateKind, "planId") : new Parsed(
                MessageType.Notification, "session/update", AgentEventKinds.Plan,
                null, payload, sessionId, RequestId: requestId,
                PlanId: planId, PatchOp: "upsert", EntityKind: "plan"),
            "available_commands_update" => new Parsed(
                MessageType.Notification, "session/update", AgentEventKinds.Commands,
                null, payload, sessionId, RequestId: requestId),
            "config_option_update" or "session_info_update" => new Parsed(
                MessageType.Notification, "session/update", AgentEventKinds.SessionInfo,
                null, payload, sessionId, RequestId: requestId),
            "usage_update" => new Parsed(
                MessageType.Notification, "session/update", AgentEventKinds.Metric,
                null, payload, sessionId, RequestId: requestId),
            // Unknown/future discriminants — preserved raw, never throw.
            _ => new Parsed(
                MessageType.Notification, "session/update", AgentEventKinds.Activity,
                ExtractText(update), payload, sessionId, RequestId: requestId)
        };
    }

    /// <summary>
    /// v2 session/request_permission: required <c>title</c> + optional
    /// <c>description</c> and tagged <c>subject</c> (tool_call/command/unknown —
    /// preserved raw). Normalized to the same downstream payload as v1.
    /// </summary>
    private static Parsed ParsePermissionV2(JsonElement p, string? requestId, bool isRequest)
    {
        var sessionId = p.TryGetProperty("sessionId", out var sid) ? sid.GetString() : null;
        var tool = p.TryGetProperty("title", out var title) ? title.GetString() ?? string.Empty : string.Empty;
        var detail = p.TryGetProperty("description", out var desc) ? desc.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrEmpty(detail)
            && p.TryGetProperty("subject", out var subject) && subject.ValueKind == JsonValueKind.Object)
        {
            detail = subject.GetRawText();
        }

        if (string.IsNullOrEmpty(tool) && p.TryGetProperty("subject", out var subj)
            && subj.TryGetProperty("toolCall", out var tc))
        {
            tool = tc.TryGetProperty("title", out var tt) ? tt.GetString() ?? string.Empty : string.Empty;
        }

        var options = new List<string>();
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
            }
        }

        if (options.Count == 0)
        {
            options.AddRange(["allow", "deny"]);
        }

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

    private static Parsed ParsePermission(JsonElement p, string? requestId, bool isRequest)
    {
        // Shape real ACP: params { sessionId, toolCall: {...}, options: [{optionId, name, kind}] }
        // Legacy shape: params { requestId, tool, detail, options: ["allow","deny"] }
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

        // In real ACP the JSON-RPC request id is the reply correlation;
        // in the legacy shape, params.requestId.
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

        // v2 whole-message updates carry a content block array.
        if (content.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind == JsonValueKind.Object
                    && block.TryGetProperty("text", out var bt)
                    && bt.GetString() is { } blockText)
                {
                    parts.Add(blockText);
                }
            }

            return parts.Count > 0 ? string.Concat(parts) : null;
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
