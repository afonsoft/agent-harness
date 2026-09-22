using System.Text.Json;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// SPEC-20260921-acp-v2-readiness RF-201: isolates the ACP wire format per
/// negotiated protocol version. The shared session/pipeline code never reads
/// v1/v2 JSON shapes directly — it asks the dialect for request params and
/// lets the parser/tracker handle versioned semantics. One connection always
/// uses exactly one dialect.
/// </summary>
public interface IAcpDialect
{
    /// <summary>Wire protocol version this dialect speaks (1 or 2).</summary>
    int ProtocolVersion { get; }

    /// <summary>Params for the initialize request (client → agent).</summary>
    object BuildInitializeParams(AcpSessionOptions options);

    /// <summary>Params for session/new.</summary>
    object BuildSessionNewParams(AcpPeerInfo peer, string workspacePath, IReadOnlyList<object> mcpServers);

    /// <summary>
    /// Params for session/resume. <paramref name="replayFromStart"/> asks the
    /// agent to replay history (v2 replayFrom:{type:"start"}, the v1-load
    /// equivalent).
    /// </summary>
    object BuildResumeParams(string sessionId, string workspacePath,
        IReadOnlyList<object> mcpServers, bool replayFromStart = false);

    /// <summary>Params for session/prompt.</summary>
    object BuildPromptParams(string? sessionId, string text, string delivery);

    /// <summary>Authentication method name ("authenticate" v1, "auth/login" v2).</summary>
    string AuthenticateMethod { get; }

    /// <summary>v1 session/load exists; v2 removed it (session/resume+replayFrom instead).</summary>
    bool SupportsSessionLoad { get; }

    /// <summary>v1 session/set_mode exists; v2 modes are config options.</summary>
    bool SupportsSetMode { get; }

    /// <summary>v1 advertises/serves client-side fs/*+terminal/*; v2 removed them.</summary>
    bool SupportsClientTools { get; }

    /// <summary>
    /// v1: the session/prompt JSON-RPC response ends the turn (stopReason).
    /// v2: the response is only an ack — the turn ends on state_update(idle).
    /// </summary>
    bool PromptResponseEndsTurn { get; }

    /// <summary>Dialect-specific turn tracker (RF-204).</summary>
    ITurnTracker CreateTurnTracker();
}

/// <summary>Dialect registry keyed by negotiated protocol version.</summary>
public static class AcpDialects
{
    public const int MaxSupported = 2;

    public static bool IsSupported(int protocolVersion) => protocolVersion is 1 or 2;

    public static IAcpDialect For(int protocolVersion) => protocolVersion switch
    {
        1 => AcpV1Dialect.Instance,
        2 => AcpV2Dialect.Instance,
        _ => throw new NotSupportedException($"ACP protocolVersion {protocolVersion} is not supported.")
    };
}

/// <summary>
/// SPEC-20260921-acp-v2-readiness RF-204: dialect-specific turn tracking.
/// v1 ends a turn when the session/prompt response arrives; v2 ends it on a
/// state_update notification with state "idle" carrying the stopReason.
/// </summary>
public interface ITurnTracker
{
    /// <summary>True while a prompt turn is open.</summary>
    bool TurnOpen { get; }

    /// <summary>Mark a turn opened (the prompt was written to the wire).</summary>
    void BeginTurn();

    /// <summary>
    /// Feed the session/prompt JSON-RPC response. Returns true and sets
    /// <paramref name="stopReason"/> when the response itself ends the turn.
    /// </summary>
    bool OnPromptResponse(JsonElement result, out string? stopReason);

    /// <summary>
    /// Feed a session/update payload (the <c>update</c> object). Returns true
    /// and sets <paramref name="stopReason"/> when the update ends the turn.
    /// </summary>
    bool OnSessionUpdate(JsonElement update, out string? stopReason);

    /// <summary>Force-close an open turn (timeout/channel death). Returns true if a turn was open.</summary>
    bool Abort();
}
