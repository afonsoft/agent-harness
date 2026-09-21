namespace Taskboard.Agents;

/// <summary>
/// Mensagem de log individual capturada da execução de um agente CLI.
/// </summary>
public sealed record AgentLogMessage(
    DateTimeOffset Timestamp,
    string IssueId,
    AgentLogStream Stream,
    string Content,
    /// <summary>Normalized <see cref="AgentEventKinds"/> when the transport parsed structure; null = raw output.</summary>
    string? Kind = null,
    /// <summary>Structured payload of the parsed message (JSON-RPC notification params, tool call, etc).</summary>
    string? PayloadJson = null);
