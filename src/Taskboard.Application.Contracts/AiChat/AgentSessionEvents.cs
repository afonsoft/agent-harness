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
/// <param name="MessageId">ACP v2 message identity (required on v2 message chunks/upserts); null in v1.</param>
/// <param name="PlanId">ACP v2 plan identity for plan_update upserts; null in v1.</param>
/// <param name="PatchOp">ACP v2 patch semantics for the event ("upsert" or "append"); null in v1.</param>
/// <param name="EntityKind">ACP v2 upserted entity kind ("message", "tool_call", "plan", "terminal"); null in v1.</param>
public sealed record AgentSessionEvent(
    string ThreadId,
    DateTimeOffset Timestamp,
    string Kind,
    string? Role = null,
    string? Content = null,
    string? PayloadJson = null,
    string? SessionId = null,
    string? ToolCallId = null,
    string? MessageId = null,
    string? PlanId = null,
    string? PatchOp = null,
    string? EntityKind = null);

/// <summary>
/// Estado do ciclo de vida da sessão interativa do agente.
/// </summary>
public sealed record AgentSessionStateInfo(
    string State,
    string? AgentType = null,
    string? WorkspacePath = null);
