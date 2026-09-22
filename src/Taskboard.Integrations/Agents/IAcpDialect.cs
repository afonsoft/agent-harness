using System.Text.Json;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Request to reattach to a previously known agent session.
/// <paramref name="ExpectsReplay"/> marks flows where the agent streams the
/// session history back as notifications before answering (v1
/// <c>session/load</c>, v2 <c>session/resume</c> + <c>replayFrom</c>) — the
/// caller uses a longer timeout for those.
/// </summary>
public sealed record AcpReattachRequest(string Method, object Params, bool ExpectsReplay);

/// <summary>
/// Version-aware ACP dialect (SPEC-20260921-acp-v2-readiness RF-201). The
/// session client stays transport- and lifecycle-generic; every version-
/// sensitive surface — handshake shape, outbound method names, session
/// update taxonomy, permission shape and turn lifecycle — lives behind this
/// contract so ACP v2 can be adopted per connection without surgery.
/// </summary>
public interface IAcpDialect
{
    /// <summary>Wire protocol version this dialect speaks.</summary>
    int ProtocolVersion { get; }

    /// <summary><c>initialize</c> params in this dialect's shape.</summary>
    object BuildInitializeParams(AcpSessionOptions options);

    /// <summary>Parses the <c>initialize</c> result into the normalized peer model.</summary>
    AcpPeerInfo ParseInitializeResult(JsonElement result);

    /// <summary><c>session/new</c> params (v2 makes <c>mcpServers</c> optional).</summary>
    object BuildSessionNewParams(AcpPeerInfo peer, string cwd, object[] mcpServers);

    /// <summary>
    /// Method + params to reattach a previous session, or null when the
    /// dialect/peer cannot reattach (v1 prefers <c>session/resume</c>, falls
    /// back to <c>session/load</c>; v2 uses <c>session/resume</c> with
    /// <c>replayFrom</c>).
    /// </summary>
    AcpReattachRequest? BuildReattachRequest(AcpPeerInfo peer, string sessionId, string cwd, object[] mcpServers);

    /// <summary><c>session/prompt</c> params.</summary>
    object BuildPromptParams(string sessionId, string text);

    /// <summary><c>session/cancel</c> notification params.</summary>
    object BuildCancelParams(string? sessionId);

    /// <summary>Auth method name: <c>authenticate</c> (v1) or <c>auth/login</c> (v2).</summary>
    string AuthenticateMethod { get; }

    /// <summary>Logout method name: <c>logout</c> (v1) or <c>auth/logout</c> (v2).</summary>
    string LogoutMethod { get; }

    /// <summary><c>session/set_mode</c> exists only in v1 — null in v2 (modes became config options).</summary>
    string? SetModeMethod { get; }

    /// <summary><c>session/set_config_option</c> params — v2 requires the <c>type</c> discriminator.</summary>
    object BuildSetConfigOptionParams(string sessionId, string configId, string value, bool isBoolean);

    /// <summary>Whether the negotiated peer accepts an MCP server over <paramref name="transport"/> ("stdio"/"http").</summary>
    bool SupportsMcpTransport(AcpPeerInfo peer, string transport);

    /// <summary>Parses <c>session/update</c> params into the normalized event shape.</summary>
    AcpProtocolParser.Parsed ParseSessionUpdate(JsonElement updateParams, string? requestId);

    /// <summary>Parses <c>session/request_permission</c> params into the normalized permission shape.</summary>
    AcpProtocolParser.Parsed ParsePermission(JsonElement p, string? requestId, bool isRequest);

    /// <summary>Dialect-specific turn lifecycle tracker (prompt-response vs state_update driven).</summary>
    ITurnTracker CreateTurnTracker();

    /// <summary>
    /// Whether an agent→client method is part of this dialect's client
    /// surface. v2 removed <c>fs/*</c> and <c>terminal/*</c> (client tools
    /// now come from MCP servers) — those get -32601 instead of dispatch.
    /// </summary>
    bool AllowsClientMethod(string method);
}

/// <summary>Dialect registry keyed by negotiated <c>protocolVersion</c>.</summary>
public static class AcpDialects
{
    /// <summary>Highest protocol version this client can speak.</summary>
    public const int MaxSupported = 2;

    public static IAcpDialect V1 { get; } = new AcpV1Dialect();
    public static IAcpDialect V2 { get; } = new AcpV2Dialect();

    /// <summary>Returns the dialect for <paramref name="version"/>, or null when unsupported.</summary>
    public static IAcpDialect? For(int version) => version switch
    {
        1 => V1,
        2 => V2,
        _ => null
    };
}
