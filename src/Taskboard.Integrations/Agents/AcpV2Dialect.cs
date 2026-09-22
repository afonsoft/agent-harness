using System.Text.Json;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// ACP v2 wire format (SPEC-20260921-acp-v2-readiness RF-205) — experimental,
/// only ever active when Taskboard:Acp:MaxProtocolVersion=2 AND the agent
/// negotiates v2. Capabilities were reorganized (one "capabilities"+"info" on
/// both sides), client-side fs/terminal methods are gone, modes became config
/// options, and the prompt lifecycle is ack + state_update instead of a
/// blocking response.
/// </summary>
public sealed class AcpV2Dialect : IAcpDialect
{
    public static readonly AcpV2Dialect Instance = new();

    private AcpV2Dialect()
    {
    }

    public int ProtocolVersion => 2;

    public object BuildInitializeParams(AcpSessionOptions options) => new
    {
        protocolVersion = 2,
        info = new { name = "taskboard", title = "Harness", version = "1.0.0" },
        // Stable v2 defines no standard client capability fields — fs/terminal
        // execution were removed and terminal display is baseline behavior.
        capabilities = new { }
    };

    public object BuildSessionNewParams(AcpPeerInfo peer, string workspacePath, IReadOnlyList<object> mcpServers) =>
        // v2: mcpServers is optional — omit rather than send an empty array.
        mcpServers.Count > 0
            ? new { cwd = workspacePath, mcpServers }
            : (object)new { cwd = workspacePath };

    public object BuildResumeParams(string sessionId, string workspacePath,
        IReadOnlyList<object> mcpServers, bool replayFromStart = false)
    {
        if (replayFromStart)
        {
            return mcpServers.Count > 0
                ? new { sessionId, cwd = workspacePath, mcpServers, replayFrom = new { type = "start" } }
                : (object)new { sessionId, cwd = workspacePath, replayFrom = new { type = "start" } };
        }

        return mcpServers.Count > 0
            ? new { sessionId, cwd = workspacePath, mcpServers }
            : (object)new { sessionId, cwd = workspacePath };
    }

    public object BuildPromptParams(string? sessionId, string text, string delivery) =>
        // Shape unchanged from v1; only the response semantics changed.
        sessionId is { } sid
            ? new { sessionId = sid, prompt = new[] { new { type = "text", text } } }
            : (object)new { text, delivery };

    public string AuthenticateMethod => "auth/login";
    public bool SupportsSessionLoad => false;
    public bool SupportsSetMode => false;
    public bool SupportsClientTools => false;
    public bool PromptResponseEndsTurn => false;

    public ITurnTracker CreateTurnTracker() => new AcpV2TurnTracker();

    /// <summary>
    /// v2 turn: the session/prompt response is an ack carrying the agent-owned
    /// messageId; the turn ends when state_update reports "idle" (stopReason
    /// included — "cancelled" confirms session/cancel).
    /// </summary>
    private sealed class AcpV2TurnTracker : ITurnTracker
    {
        public bool TurnOpen { get; private set; }

        /// <summary>Agent-owned id of the inserted user message (from the prompt ack).</summary>
        public string? PromptMessageId { get; private set; }

        public void BeginTurn() => TurnOpen = true;

        public bool OnPromptResponse(JsonElement result, out string? stopReason)
        {
            PromptMessageId = result.TryGetProperty("messageId", out var mid) ? mid.GetString() : null;
            stopReason = null;
            return false;
        }

        public bool OnSessionUpdate(JsonElement update, out string? stopReason)
        {
            stopReason = null;
            if (!TurnOpen
                || !update.TryGetProperty("sessionUpdate", out var su)
                || su.GetString() != "state_update"
                || !update.TryGetProperty("state", out var st)
                || st.GetString() != "idle")
            {
                return false;
            }

            TurnOpen = false;
            PromptMessageId = null;
            stopReason = update.TryGetProperty("stopReason", out var sr) ? sr.GetString() : null;
            return true;
        }

        public bool Abort()
        {
            var was = TurnOpen;
            TurnOpen = false;
            PromptMessageId = null;
            return was;
        }
    }
}
