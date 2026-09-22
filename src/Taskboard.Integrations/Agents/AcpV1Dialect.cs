using System.Text.Json;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// ACP v1 wire format — the dialect the client has always spoken
/// (SPEC-20260921-acp-v2-readiness RF-201). Stateless: a single instance
/// serves every v1 connection.
/// </summary>
public sealed class AcpV1Dialect : IAcpDialect
{
    public static readonly AcpV1Dialect Instance = new();

    private AcpV1Dialect()
    {
    }

    public int ProtocolVersion => 1;

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

    public object BuildSessionNewParams(AcpPeerInfo peer, string workspacePath, IReadOnlyList<object> mcpServers)
    {
        if (peer.AdditionalDirectories)
        {
            return new { cwd = workspacePath, mcpServers, additionalDirectories = Array.Empty<string>() };
        }

        return new { cwd = workspacePath, mcpServers };
    }

    public object BuildResumeParams(string sessionId, string workspacePath,
        IReadOnlyList<object> mcpServers, bool replayFromStart = false) => new
        {
            sessionId,
            cwd = workspacePath,
            mcpServers
        };

    public object BuildPromptParams(string? sessionId, string text, string delivery) =>
        sessionId is { } sid
            ? new { sessionId = sid, prompt = new[] { new { type = "text", text } } }
            : (object)new { text, delivery };

    public string AuthenticateMethod => "authenticate";
    public bool SupportsSessionLoad => true;
    public bool SupportsSetMode => true;
    public bool SupportsClientTools => true;
    public bool PromptResponseEndsTurn => true;

    public ITurnTracker CreateTurnTracker() => new AcpV1TurnTracker();

    /// <summary>v1 turn: the session/prompt response ends it (stopReason in result).</summary>
    private sealed class AcpV1TurnTracker : ITurnTracker
    {
        public bool TurnOpen { get; private set; }

        public void BeginTurn() => TurnOpen = true;

        public bool OnPromptResponse(JsonElement result, out string? stopReason)
        {
            TurnOpen = false;
            stopReason = result.TryGetProperty("stopReason", out var sr) ? sr.GetString() : null;
            return true;
        }

        public bool OnSessionUpdate(JsonElement update, out string? stopReason)
        {
            stopReason = null;
            return false;
        }

        public bool Abort()
        {
            var was = TurnOpen;
            TurnOpen = false;
            return was;
        }
    }
}
