namespace Taskboard.Application.Contracts.AiChat;

/// <summary>
/// Informações estruturadas de uma chamada de ferramenta realizada pelo agente.
/// </summary>
public sealed record ToolCallInfo(
    string Id,
    string Name,
    string? Arguments = null,
    string Status = "running",
    string? Output = null,
    string? Diff = null);

/// <summary>
/// Informações estruturadas de uma requisição de permissão emitida pelo agente.
/// </summary>
public sealed record PermissionRequestInfo(
    string RequestId,
    string Tool,
    string Detail,
    IReadOnlyList<string> Options);

/// <summary>
/// Structured agent session event emitted to the thread.
/// </summary>
/// <param name="SessionId">ACP session id when the event carries one (session/update notifications).</param>
/// <param name="ToolCallId">Tool call correlation id for tool_call/tool_call_update events.</param>
public sealed record AgentSessionEvent(
    string ThreadId,
    DateTimeOffset Timestamp,
    string Kind,
    string? Role = null,
    string? Content = null,
    string? PayloadJson = null,
    string? SessionId = null,
    string? ToolCallId = null);

/// <summary>
/// Estado do ciclo de vida da sessão interativa do agente.
/// </summary>
public sealed record AgentSessionStateInfo(
    string State,
    string? AgentType = null,
    string? WorkspacePath = null);
