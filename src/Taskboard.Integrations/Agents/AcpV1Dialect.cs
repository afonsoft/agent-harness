using System.Text.Json;
using Taskboard.Agents;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// ACP v1 dialect — the behavior shipped by SPEC-20260921-acp-v1-conformance.
/// Selected for every connection that negotiates <c>protocolVersion: 1</c>
/// (the default; v2 stays opt-in while draft).
/// </summary>
public sealed class AcpV1Dialect : IAcpDialect
{
    public int ProtocolVersion => 1;

    public string AuthenticateMethod => "authenticate";

    public string LogoutMethod => "logout";

    public string? SetModeMethod => "session/set_mode";

    public object BuildInitializeParams(AcpSessionOptions options) => new
    {
        protocolVersion = 1,
        clientCapabilities = new
        {
            fs = new { readTextFile = options.ClientFs, writeTextFile = options.ClientFs },
            terminal = options.ClientTerminal,
            auth = new { terminal = options.TerminalAuth },
            session = new { configOptions = new { boolean = options.BooleanConfigOptions ? new { } : (object?)null } }
        },
        clientInfo = new { name = "taskboard", title = "Harness", version = "1.0.0" }
    };

    public AcpPeerInfo ParseInitializeResult(JsonElement result) => AcpPeerInfo.FromInitialize(result);

    public object BuildSessionNewParams(AcpPeerInfo peer, string cwd, object[] mcpServers)
    {
        if (peer.AdditionalDirectories)
        {
            return new { cwd, mcpServers, additionalDirectories = Array.Empty<string>() };
        }

        return new { cwd, mcpServers };
    }

    public AcpReattachRequest? BuildReattachRequest(
        AcpPeerInfo peer, string sessionId, string cwd, object[] mcpServers)
    {
        object p = new { sessionId, cwd, mcpServers };
        if (peer.SessionResume)
        {
            return new AcpReattachRequest("session/resume", p, ExpectsReplay: false);
        }

        // session/load replays history as session/update notifications before responding.
        return peer.LoadSession
            ? new AcpReattachRequest("session/load", p, ExpectsReplay: true)
            : null;
    }

    public object BuildPromptParams(string sessionId, string text) =>
        new { sessionId, prompt = new[] { new { type = "text", text } } };

    public object BuildCancelParams(string? sessionId) =>
        sessionId is { } sid ? new { sessionId = sid } : new { };

    public object BuildSetConfigOptionParams(string sessionId, string configId, string value, bool isBoolean) =>
        isBoolean
            ? new { sessionId, configId, type = "boolean", value = bool.Parse(value) }
            : (object)new { sessionId, configId, value };

    public bool SupportsMcpTransport(AcpPeerInfo peer, string transport) =>
        !string.Equals(transport, "http", StringComparison.OrdinalIgnoreCase) || peer.McpHttp;

    public AcpProtocolParser.Parsed ParseSessionUpdate(JsonElement p, string? requestId)
    {
        var sessionId = p.TryGetProperty("sessionId", out var sid) ? sid.GetString() : null;

        if (!p.TryGetProperty("update", out var update) || update.ValueKind != JsonValueKind.Object)
        {
            // Legacy shape: session/update with direct params.kind/content.
            if (p.TryGetProperty("kind", out var legacyKind))
            {
                return new AcpProtocolParser.Parsed(
                    AcpProtocolParser.MessageType.Notification, "session/update",
                    legacyKind.GetString() ?? "message",
                    p.TryGetProperty("content", out var lc) ? lc.GetString() : null,
                    p.GetRawText(), sessionId, RequestId: requestId);
            }

            return new AcpProtocolParser.Parsed(
                AcpProtocolParser.MessageType.Notification, "session/update", "activity", null,
                p.GetRawText(), SessionId: sessionId, RequestId: requestId);
        }

        var updateKind = update.TryGetProperty("sessionUpdate", out var su)
            ? su.GetString() ?? string.Empty
            : string.Empty;

        var toolCallId = update.TryGetProperty("toolCallId", out var tcid) ? tcid.GetString() : null;
        var payload = update.GetRawText();

        return updateKind switch
        {
            "agent_message_chunk" => new AcpProtocolParser.Parsed(
                AcpProtocolParser.MessageType.Notification, "session/update", AgentEventKinds.Message,
                AcpProtocolParser.ExtractText(update), payload, sessionId, RequestId: requestId),
            "agent_thought_chunk" => new AcpProtocolParser.Parsed(
                AcpProtocolParser.MessageType.Notification, "session/update", AgentEventKinds.Thought,
                AcpProtocolParser.ExtractText(update), payload, sessionId, RequestId: requestId),
            // RF-007: replayed user messages (session/load) and agent-advertised
            // slash commands / mode / config / session metadata all get their
            // own normalized kinds instead of falling into generic activity.
            "user_message_chunk" => new AcpProtocolParser.Parsed(
                AcpProtocolParser.MessageType.Notification, "session/update", AgentEventKinds.Message,
                AcpProtocolParser.ExtractText(update), payload, sessionId, RequestId: requestId),
            "available_commands_update" => new AcpProtocolParser.Parsed(
                AcpProtocolParser.MessageType.Notification, "session/update", AgentEventKinds.Commands,
                null, payload, sessionId, RequestId: requestId),
            "current_mode_update" or "config_option_update" or "session_info_update" => new AcpProtocolParser.Parsed(
                AcpProtocolParser.MessageType.Notification, "session/update", AgentEventKinds.SessionInfo,
                null, payload, sessionId, RequestId: requestId),
            "tool_call" => new AcpProtocolParser.Parsed(
                AcpProtocolParser.MessageType.Notification, "session/update", AgentEventKinds.ToolCall,
                update.TryGetProperty("title", out var t) ? t.GetString() : null,
                payload, sessionId, toolCallId, requestId),
            "tool_call_update" => new AcpProtocolParser.Parsed(
                AcpProtocolParser.MessageType.Notification, "session/update", AgentEventKinds.ToolOutput,
                null, payload, sessionId, toolCallId, requestId),
            "plan" => new AcpProtocolParser.Parsed(
                AcpProtocolParser.MessageType.Notification, "session/update", AgentEventKinds.Plan,
                null, payload, sessionId, RequestId: requestId),
            "usage_update" => new AcpProtocolParser.Parsed(
                AcpProtocolParser.MessageType.Notification, "session/update", AgentEventKinds.Metric,
                null, payload, sessionId, RequestId: requestId),
            _ => new AcpProtocolParser.Parsed(
                AcpProtocolParser.MessageType.Notification, "session/update", AgentEventKinds.Activity,
                AcpProtocolParser.ExtractText(update), payload, sessionId, RequestId: requestId)
        };
    }

    public AcpProtocolParser.Parsed ParsePermission(JsonElement p, string? requestId, bool isRequest)
    {
        // Shape real ACP: params { sessionId, toolCall: {...}, options: [{optionId, name, kind}] }
        // Legacy shape: params { requestId, tool, detail, options: ["allow","deny"] }
        var sessionId = p.TryGetProperty("sessionId", out var sid) ? sid.GetString() : null;
        var tool = string.Empty;
        var detail = string.Empty;
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

        return new AcpProtocolParser.Parsed(
            isRequest ? AcpProtocolParser.MessageType.Request : AcpProtocolParser.MessageType.Notification,
            "session/request_permission",
            AgentEventKinds.Permission,
            detail,
            payload,
            sessionId,
            RequestId: requestId);
    }

    public ITurnTracker CreateTurnTracker() => new AcpV1TurnTracker();

    public bool AllowsClientMethod(string method) => true;
}
