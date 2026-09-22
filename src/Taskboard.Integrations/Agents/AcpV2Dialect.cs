using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Taskboard.Agents;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// ACP v2 dialect (draft — SPEC-20260921-acp-v2-readiness RF-205). Selected
/// only when the negotiated <c>protocolVersion</c> is 2, which requires
/// <c>Taskboard:Acp:MaxProtocolVersion=2</c> — strictly opt-in while the spec
/// is draft. Differences vs v1: <c>info</c>/<c>capabilities</c> handshake
/// shape, whole-message + chunk upserts keyed by <c>messageId</c>,
/// <c>state_update</c>-driven turn lifecycle, upsert-only
/// <c>tool_call_update</c>, <c>plan_update</c> keyed by <c>planId</c>,
/// <c>auth/login</c>, no <c>fs/*</c>/<c>terminal/*</c>/<c>session/load</c>/
/// <c>session/set_mode</c>.
/// </summary>
public sealed class AcpV2Dialect : IAcpDialect
{
    public int ProtocolVersion => 2;

    public string AuthenticateMethod => "auth/login";

    public string LogoutMethod => "auth/logout";

    /// <summary>v2 removed modes — mode switching is a config option now.</summary>
    public string? SetModeMethod => null;

    public object BuildInitializeParams(AcpSessionOptions options) => new
    {
        protocolVersion = 2,
        // v2 renamed clientInfo→info; stable v2 defines no standard client
        // capability fields (fs/terminal client methods were removed).
        info = new { name = "taskboard", title = "Harness", version = "1.0.0" },
        capabilities = new { }
    };

    public AcpPeerInfo ParseInitializeResult(JsonElement result) => AcpPeerInfo.FromInitializeV2(result);

    public object BuildSessionNewParams(AcpPeerInfo peer, string cwd, object[] mcpServers)
    {
        // v2: mcpServers is optional — omit when empty.
        var node = new JsonObject { ["cwd"] = cwd };
        if (mcpServers.Length > 0)
        {
            node["mcpServers"] = JsonSerializer.SerializeToNode(mcpServers);
        }

        if (peer.AdditionalDirectories)
        {
            node["additionalDirectories"] = new JsonArray();
        }

        return node;
    }

    public AcpReattachRequest? BuildReattachRequest(
        AcpPeerInfo peer, string sessionId, string cwd, object[] mcpServers)
    {
        if (!peer.SessionResume)
        {
            return null;
        }

        // v2 has no session/load — replayFrom:{type:"start"} replays history.
        var node = new JsonObject
        {
            ["sessionId"] = sessionId,
            ["cwd"] = cwd,
            ["replayFrom"] = new JsonObject { ["type"] = "start" }
        };
        if (mcpServers.Length > 0)
        {
            node["mcpServers"] = JsonSerializer.SerializeToNode(mcpServers);
        }

        return new AcpReattachRequest("session/resume", node, ExpectsReplay: true);
    }

    public object BuildPromptParams(string sessionId, string text) =>
        new { sessionId, prompt = new[] { new { type = "text", text } } };

    public object BuildCancelParams(string? sessionId) =>
        sessionId is { } sid ? new { sessionId = sid } : new { };

    public object BuildSetConfigOptionParams(string sessionId, string configId, string value, bool isBoolean) =>
        isBoolean
            ? new { sessionId, configId, type = "boolean", value = bool.Parse(value) }
            : (object)new { sessionId, configId, type = "id", value };

    public bool SupportsMcpTransport(AcpPeerInfo peer, string transport) =>
        string.Equals(transport, "stdio", StringComparison.OrdinalIgnoreCase)
            ? peer.McpStdio
            : peer.McpHttp;

    public AcpProtocolParser.Parsed ParseSessionUpdate(JsonElement p, string? requestId)
    {
        var sessionId = p.TryGetProperty("sessionId", out var sid) ? sid.GetString() : null;

        if (!p.TryGetProperty("update", out var update) || update.ValueKind != JsonValueKind.Object)
        {
            return new AcpProtocolParser.Parsed(
                AcpProtocolParser.MessageType.Notification, "session/update", "activity", null,
                p.GetRawText(), SessionId: sessionId, RequestId: requestId);
        }

        var updateKind = update.TryGetProperty("sessionUpdate", out var su)
            ? su.GetString() ?? string.Empty
            : string.Empty;

        var payload = update.GetRawText();
        var toolCallId = update.TryGetProperty("toolCallId", out var tcid) ? tcid.GetString() : null;
        var messageId = update.TryGetProperty("messageId", out var mid) ? mid.GetString() : null;

        return updateKind switch
        {
            // Whole-message upserts — content array replaces (null/[] clears,
            // omitted merges over the keyed messageId entity).
            "user_message" => WholeMessage(update, payload, sessionId, requestId, messageId,
                AgentEventKinds.Message, "user"),
            "agent_message" => WholeMessage(update, payload, sessionId, requestId, messageId,
                AgentEventKinds.Message, "assistant"),
            "agent_thought" => WholeMessage(update, payload, sessionId, requestId, messageId,
                AgentEventKinds.Thought, "assistant"),

            // Chunked streaming — appends into the keyed messageId entity.
            "user_message_chunk" => Chunk(update, payload, sessionId, requestId, messageId,
                AgentEventKinds.Message, "user"),
            "agent_message_chunk" => Chunk(update, payload, sessionId, requestId, messageId,
                AgentEventKinds.Message, "assistant"),
            "agent_thought_chunk" => Chunk(update, payload, sessionId, requestId, messageId,
                AgentEventKinds.Thought, "assistant"),

            // v2 turn lifecycle: running/idle/requires_action; idle+stopReason
            // closes the turn (see AcpV2TurnTracker).
            "state_update" => new AcpProtocolParser.Parsed(
                AcpProtocolParser.MessageType.Notification, "session/update", AgentEventKinds.Lifecycle,
                update.TryGetProperty("state", out var st) ? st.GetString() : null,
                payload, sessionId, RequestId: requestId),

            // Upsert-only tool calls — the client resolves first-seen →
            // tool_call / patch → tool_output per session.
            "tool_call_update" => new AcpProtocolParser.Parsed(
                AcpProtocolParser.MessageType.Notification, "session/update", AgentEventKinds.ToolCall,
                update.TryGetProperty("title", out var tt) ? tt.GetString() : null,
                payload, sessionId, toolCallId, requestId,
                PatchOp: AgentPatchOps.Replace, IsToolCallUpsert: true),
            "tool_call_content_chunk" => new AcpProtocolParser.Parsed(
                AcpProtocolParser.MessageType.Notification, "session/update", AgentEventKinds.ToolOutput,
                AcpProtocolParser.ExtractText(update), payload, sessionId, toolCallId, requestId,
                PatchOp: AgentPatchOps.Append),

            // plan_update replaces that plan's entries — keyed by plan.planId.
            "plan_update" => PlanUpdate(update, payload, sessionId, requestId),

            // Display-only terminal surface (client-side terminal/* removed).
            "terminal_update" or "terminal_output_chunk" => new AcpProtocolParser.Parsed(
                AcpProtocolParser.MessageType.Notification, "session/update", AgentEventKinds.Terminal,
                TerminalText(update), payload, sessionId, RequestId: requestId),

            "available_commands_update" => new AcpProtocolParser.Parsed(
                AcpProtocolParser.MessageType.Notification, "session/update", AgentEventKinds.Commands,
                null, payload, sessionId, RequestId: requestId),
            "config_option_update" or "session_info_update" or "current_mode_update" => new AcpProtocolParser.Parsed(
                AcpProtocolParser.MessageType.Notification, "session/update", AgentEventKinds.SessionInfo,
                null, payload, sessionId, RequestId: requestId),
            "usage_update" => new AcpProtocolParser.Parsed(
                AcpProtocolParser.MessageType.Notification, "session/update", AgentEventKinds.Metric,
                null, payload, sessionId, RequestId: requestId),

            // Forward-compat: unknown and _-prefixed variants are preserved raw.
            _ => new AcpProtocolParser.Parsed(
                AcpProtocolParser.MessageType.Notification, "session/update", AgentEventKinds.Activity,
                AcpProtocolParser.ExtractText(update), payload, sessionId, RequestId: requestId)
        };
    }

    /// <summary>Whole-message upsert — PatchOp derived from content omit/null/value.</summary>
    private static AcpProtocolParser.Parsed WholeMessage(
        JsonElement update, string payload, string? sessionId, string? requestId,
        string? messageId, string kind, string role)
    {
        string? content;
        string patchOp;
        if (!update.TryGetProperty("content", out var c))
        {
            // Metadata-only update — merge leaves the accumulated content intact.
            content = null;
            patchOp = AgentPatchOps.Replace;
        }
        else if (c.ValueKind == JsonValueKind.Null
                 || (c.ValueKind == JsonValueKind.Array && c.GetArrayLength() == 0))
        {
            content = null;
            patchOp = AgentPatchOps.Clear;
        }
        else
        {
            content = AcpProtocolParser.ExtractText(update);
            patchOp = AgentPatchOps.Replace;
        }

        return new AcpProtocolParser.Parsed(
            AcpProtocolParser.MessageType.Notification, "session/update", kind,
            content, payload, sessionId, RequestId: requestId,
            MessageId: messageId, PatchOp: patchOp, Role: role);
    }

    private static AcpProtocolParser.Parsed Chunk(
        JsonElement update, string payload, string? sessionId, string? requestId,
        string? messageId, string kind, string role) =>
        new(AcpProtocolParser.MessageType.Notification, "session/update", kind,
            AcpProtocolParser.ExtractText(update), payload, sessionId, RequestId: requestId,
            MessageId: messageId, PatchOp: AgentPatchOps.Append, Role: role);

    /// <summary>Hoists plan.entries to the payload root so existing plan rendering works.</summary>
    private static AcpProtocolParser.Parsed PlanUpdate(
        JsonElement update, string payload, string? sessionId, string? requestId)
    {
        string? planId = null;
        var normalized = payload;
        if (update.TryGetProperty("plan", out var plan) && plan.ValueKind == JsonValueKind.Object)
        {
            planId = plan.TryGetProperty("planId", out var pid) ? pid.GetString() : null;
            try
            {
                var node = JsonNode.Parse(plan.GetRawText())?.AsObject();
                if (node is not null)
                {
                    node["sessionUpdate"] = "plan_update";
                    normalized = node.ToJsonString();
                }
            }
            catch (JsonException)
            {
                // keep the raw update as payload
            }
        }

        return new AcpProtocolParser.Parsed(
            AcpProtocolParser.MessageType.Notification, "session/update", AgentEventKinds.Plan,
            null, normalized, sessionId, RequestId: requestId,
            PlanId: planId, PatchOp: AgentPatchOps.Replace);
    }

    /// <summary>Display text for terminal updates — decodes the base64 data field.</summary>
    private static string? TerminalText(JsonElement update)
    {
        if (update.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Object
            && output.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.String)
        {
            return DecodeBase64(data.GetString());
        }

        if (update.TryGetProperty("data", out var direct) && direct.ValueKind == JsonValueKind.String)
        {
            return DecodeBase64(direct.GetString());
        }

        return update.TryGetProperty("command", out var cmd) ? cmd.GetString() : null;
    }

    private static string? DecodeBase64(string? data)
    {
        if (data is null)
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(data));
        }
        catch (FormatException)
        {
            return data;
        }
    }

    public AcpProtocolParser.Parsed ParsePermission(JsonElement p, string? requestId, bool isRequest)
    {
        // v2: params { sessionId, title, subject, options:[{optionId,name,kind}] };
        // tolerate the v1 toolCall shape for draft implementations.
        var sessionId = p.TryGetProperty("sessionId", out var sid) ? sid.GetString() : null;
        var tool = p.TryGetProperty("title", out var title) ? title.GetString() ?? string.Empty : string.Empty;
        var detail = p.TryGetProperty("subject", out var subject)
            ? subject.ValueKind == JsonValueKind.String ? subject.GetString() ?? string.Empty : subject.GetRawText()
            : string.Empty;
        var options = new List<string>();

        if (p.TryGetProperty("toolCall", out var toolCall) && toolCall.ValueKind == JsonValueKind.Object)
        {
            if (string.IsNullOrEmpty(tool))
            {
                tool = toolCall.TryGetProperty("title", out var tt) ? tt.GetString() ?? string.Empty : string.Empty;
            }

            if (string.IsNullOrEmpty(detail))
            {
                detail = toolCall.TryGetProperty("rawInput", out var ri) ? ri.GetRawText() : tool;
            }
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

        return new AcpProtocolParser.Parsed(
            isRequest ? AcpProtocolParser.MessageType.Request : AcpProtocolParser.MessageType.Notification,
            "session/request_permission",
            AgentEventKinds.Permission,
            detail,
            payload,
            sessionId,
            RequestId: requestId);
    }

    public ITurnTracker CreateTurnTracker() => new AcpV2TurnTracker();

    /// <summary>
    /// v2 removed the client-side fs/* and terminal/* methods (client tools
    /// are MCP servers now) — they must never be dispatched on a v2
    /// connection; everything else (elicitation, _extensions) still flows to
    /// the handler or gets -32601 there.
    /// </summary>
    public bool AllowsClientMethod(string method) =>
        !method.StartsWith("fs/", StringComparison.Ordinal)
        && !method.StartsWith("terminal/", StringComparison.Ordinal);
}
