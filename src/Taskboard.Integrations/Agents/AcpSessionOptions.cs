using System.Text.Json;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Options for the ACP session client (SPEC-20260921-acp-v1-conformance
/// RF-005/008/009/011/012/014). Bound from <c>Taskboard:Acp:*</c> configuration;
/// the fs/terminal client surface and per-agent transport stay behind flags.
/// </summary>
public sealed class AcpSessionOptions
{
    /// <summary>Offer + serve fs/read_text_file and fs/write_text_file. Default on.</summary>
    public bool ClientFs { get; set; } = true;

    /// <summary>Offer + serve terminal/* (server-side command execution). Default off.</summary>
    public bool ClientTerminal { get; set; } = false;

    /// <summary>Advertise clientCapabilities.auth.terminal (interactive CLI login flow).</summary>
    public bool TerminalAuth { get; set; } = true;

    /// <summary>Advertise boolean session config options support.</summary>
    public bool BooleanConfigOptions { get; set; } = true;

    /// <summary>Timeout for non-turn JSON-RPC requests (initialize, session/*, fs/* replies).</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Max time a session/prompt turn may stay pending before we cancel it.</summary>
    public TimeSpan TurnTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Timeout for the initialize/session-new handshake phase.</summary>
    public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Grace period for session/close before killing the process.</summary>
    public TimeSpan CloseTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Unanswered permission requests are auto-replied "cancelled" after this.</summary>
    public TimeSpan PermissionTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>When set, connect to an already-running agent ACP server on
    /// 127.0.0.1:port (e.g. <c>copilot --acp --port N</c>) instead of spawning
    /// a subprocess (RF-012). An adapter-level <c>AgentCommand.TcpPort</c>
    /// takes precedence.</summary>
    public int? AgentTcpPort { get; set; }

    /// <summary>MCP servers injected into session/new (e.g. the Harness RAG/Knowledge MCP).</summary>
    public IReadOnlyList<AcpMcpServerSpec> McpServers { get; set; } = [];
}

/// <summary>An MCP server to hand to the agent in session/new.mcpServers.</summary>
public sealed record AcpMcpServerSpec(string Name, string? Url, string? Command,
    IReadOnlyList<string> Args, IReadOnlyDictionary<string, string>? Headers);

/// <summary>
/// Handles agent→client ACP requests (fs/*, terminal/*, elicitation/*) that the
/// session client cannot answer itself. Implemented in the Server layer where
/// PermissionGate and workspace services live. Returns the JSON-RPC result
/// payload, or throws <see cref="AcpException"/> to produce an error response.
/// Null = no handler registered → agent gets -32601.
/// </summary>
public interface IAcpClientToolHandler
{
    /// <param name="workspacePath">Session workspace root — fs paths are
    /// sandboxed to it and terminals start there by default. When empty, the
    /// handler resolves the thread's workspace itself.</param>
    Task<JsonElement> HandleAsync(
        string threadId,
        string sessionId,
        string workspacePath,
        string method,
        JsonElement @params,
        CancellationToken cancellationToken);
}
