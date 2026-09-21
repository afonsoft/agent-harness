namespace Taskboard.Agents;

/// <summary>
/// Ação de controle unificada sobre qualquer escopo de execução de agente
/// (SPEC-20260921-board-cockpit-agent-observability RF-004).
/// </summary>
public sealed record AgentControlRequest(
    /// <summary>Escopo: <c>run</c> (pipeline/cockpit), <c>thread</c> (AI Chat) ou <c>issue</c> (board one-shot).</summary>
    string ScopeKind,
    string ScopeId,
    /// <summary><c>cancel</c> | <c>steer</c> | <c>retry</c>.</summary>
    string Action,
    /// <summary>Instrução para <c>steer</c>; prompt ajustado para <c>retry</c>.</summary>
    string? Content = null,
    /// <summary>Stage alvo para <c>retry</c> em escopo <c>run</c>.</summary>
    string? StageId = null);

/// <summary>Resposta de permissão unificada por escopo.</summary>
public sealed record AgentPermissionReplyRequest(
    string ScopeKind,
    string ScopeId,
    string RequestId,
    /// <summary><c>allow</c> | <c>deny</c> | <c>always</c> (thread) ou <c>Allow</c>/<c>Deny</c> (run stage gate).</summary>
    string Outcome,
    string? Comment = null);

/// <summary>Snapshot de estado de um escopo de execução para montagem da UI.</summary>
public sealed record AgentScopeState(
    string ScopeKind,
    string ScopeId,
    /// <summary>queued | running | waiting_permission | awaiting_retry | paused | stopped | failed | completed | idle.</summary>
    string State,
    string? ActiveStageId = null,
    int PendingPermissions = 0,
    long LastEventSequence = 0);
