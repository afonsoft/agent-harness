using System.Text.Json;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Integrations.Agents;

namespace Taskboard.Server.Services;

/// <summary>Outcome hint for a unified agent control call.</summary>
public enum AgentControlStatus
{
    Accepted,
    Ok,
    Conflict,
    BadRequest,
    Gone,
    NotFound,
}

/// <summary>Result of a scope-scoped control/permission action.</summary>
public sealed record AgentControlResult(AgentControlStatus Status, object? Payload = null, string? Error = null);

/// <summary>
/// Unified agent control plane (SPEC-20260921-board-cockpit-agent-observability
/// RF-004/RF-005): routes cancel/steer/retry and permission replies by scope to
/// the owning service and audits accepted actions as normalized events. Keeps
/// the HTTP endpoints free of business rules.
/// </summary>
public sealed class AgentControlService
{
    private readonly IAgentOrchestrationService _orchestration;
    private readonly ISteerQueue _steer;
    private readonly IPipelineOrchestrator _pipelines;
    private readonly AgentSessionManager _sessions;
    private readonly PermissionGate _permissionGate;
    private readonly AcpSessionClient _sessionClient;
    private readonly IAgentRunEventRepository _eventRepository;
    private readonly IAgentExecutionEventSink _sink;

    public AgentControlService(
        IAgentOrchestrationService orchestration,
        ISteerQueue steer,
        IPipelineOrchestrator pipelines,
        AgentSessionManager sessions,
        PermissionGate permissionGate,
        AcpSessionClient sessionClient,
        IAgentRunEventRepository eventRepository,
        IAgentExecutionEventSink sink)
    {
        _orchestration = orchestration;
        _steer = steer;
        _pipelines = pipelines;
        _sessions = sessions;
        _permissionGate = permissionGate;
        _sessionClient = sessionClient;
        _eventRepository = eventRepository;
        _sink = sink;
    }

    /// <summary>Unified control action (cancel | steer | retry) per scope.</summary>
    public async Task<AgentControlResult> ExecuteAsync(AgentControlRequest request, CancellationToken cancellationToken)
    {
        var action = request.Action?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(request.ScopeId) || action is null)
        {
            return new AgentControlResult(AgentControlStatus.BadRequest, Error: "scopeId and action are required.");
        }

        switch ((request.ScopeKind, action))
        {
            case (AgentEventScope.Issue, "cancel"):
                {
                    var signalled = await _orchestration.CancelAsync(request.ScopeId, cancellationToken);
                    if (!signalled)
                    {
                        return new AgentControlResult(AgentControlStatus.Conflict, Error: "no-active-run");
                    }

                    await EmitAsync(request, AgentEventKinds.Lifecycle, "Cancel requested (board)", cancellationToken);
                    return new AgentControlResult(AgentControlStatus.Accepted);
                }

            case (AgentEventScope.Run, "steer") when !string.IsNullOrWhiteSpace(request.Content):
                {
                    if (await _pipelines.GetAsync(request.ScopeId, cancellationToken) is null)
                    {
                        return new AgentControlResult(AgentControlStatus.Conflict, Error: "no-such-run");
                    }

                    _steer.Enqueue(request.ScopeId, request.Content.Trim());
                    await EmitAsync(request, AgentEventKinds.Steer, "Steer queued",
                        JsonSerializer.Serialize(new { content = request.Content.Trim() }), cancellationToken);
                    return new AgentControlResult(AgentControlStatus.Accepted);
                }

            case (AgentEventScope.Run, "cancel"):
                {
                    var exec = await _pipelines.GetAsync(request.ScopeId, cancellationToken);
                    if (exec is null)
                    {
                        return new AgentControlResult(AgentControlStatus.Conflict, Error: "no-such-run");
                    }
                    if (exec.Status is "Completed" or "Cancelled")
                    {
                        return new AgentControlResult(AgentControlStatus.Conflict, Error: "run-already-finished");
                    }

                    await _pipelines.CancelAsync(request.ScopeId, cancellationToken);
                    await EmitAsync(request, AgentEventKinds.Lifecycle, "Cancel requested (run)", cancellationToken);
                    return new AgentControlResult(AgentControlStatus.Accepted);
                }

            case (AgentEventScope.Run, "retry") when !string.IsNullOrWhiteSpace(request.StageId):
                {
                    if (await _pipelines.GetAsync(request.ScopeId, cancellationToken) is null)
                    {
                        return new AgentControlResult(AgentControlStatus.Conflict, Error: "no-such-run");
                    }

                    var retried = await _pipelines.RetryStageAsync(request.ScopeId, request.StageId, request.Content, cancellationToken);
                    await EmitAsync(request, AgentEventKinds.Lifecycle, $"Retry dispatched for stage '{request.StageId}'", cancellationToken);
                    return new AgentControlResult(AgentControlStatus.Ok, Payload: retried);
                }

            case (AgentEventScope.Thread, "cancel"):
                {
                    var cancelled = await _sessions.CancelAsync(request.ScopeId, cancellationToken);
                    if (!cancelled)
                    {
                        return new AgentControlResult(AgentControlStatus.Conflict, Error: "no-active-session");
                    }

                    await EmitAsync(request, AgentEventKinds.Lifecycle, "Cancel requested (thread)", cancellationToken);
                    return new AgentControlResult(AgentControlStatus.Accepted);
                }

            case (AgentEventScope.Thread, "steer") when !string.IsNullOrWhiteSpace(request.Content):
                {
                    var sent = await _sessions.PromptAsync(request.ScopeId, request.Content.Trim(), "steer", cancellationToken);
                    if (!sent)
                    {
                        return new AgentControlResult(AgentControlStatus.Conflict, Error: "no-active-session");
                    }

                    await EmitAsync(request, AgentEventKinds.Steer, "Steer sent (thread)",
                        JsonSerializer.Serialize(new { content = request.Content.Trim() }), cancellationToken);
                    return new AgentControlResult(AgentControlStatus.Accepted);
                }

            // SPEC-20260921-acp-v1-conformance RF-003: session/set_config_option
            // (model, mode and agent-defined options) on a live thread session.
            case (AgentEventScope.Thread, "set_config")
                when !string.IsNullOrWhiteSpace(request.ConfigId) && request.Content is not null:
                {
                    var set = await _sessions.SetConfigOptionAsync(
                        request.ScopeId, request.ConfigId.Trim(), request.Content, cancellationToken);
                    if (!set)
                    {
                        return new AgentControlResult(AgentControlStatus.Conflict, Error: "no-active-session");
                    }

                    await EmitAsync(request, AgentEventKinds.Lifecycle, $"Config '{request.ConfigId}' → '{request.Content}'",
                        cancellationToken);
                    return new AgentControlResult(AgentControlStatus.Accepted);
                }

            case (AgentEventScope.Thread, "set_mode") when !string.IsNullOrWhiteSpace(request.Content):
                {
                    var set = await _sessions.SetModeAsync(request.ScopeId, request.Content.Trim(), cancellationToken);
                    if (!set)
                    {
                        return new AgentControlResult(AgentControlStatus.Conflict, Error: "no-active-session");
                    }

                    await EmitAsync(request, AgentEventKinds.Lifecycle, $"Mode → '{request.Content.Trim()}'", cancellationToken);
                    return new AgentControlResult(AgentControlStatus.Accepted);
                }

            case (AgentEventScope.Issue, "steer") or (AgentEventScope.Issue, "retry")
                or (AgentEventScope.Thread, "retry") or (AgentEventScope.Run, "steer"):
                return new AgentControlResult(AgentControlStatus.Conflict,
                    Error: $"action '{action}' is not supported for scope '{request.ScopeKind}' (content required or unsupported).");

            default:
                return new AgentControlResult(AgentControlStatus.BadRequest,
                    Error: $"unknown scope '{request.ScopeKind}' or action '{action}'.");
        }
    }

    /// <summary>
    /// Unified permission/approval reply: thread → ACP session gate;
    /// run → pipeline stage gate (requestId "stage:&lt;key&gt;").
    /// </summary>
    public async Task<AgentControlResult> ReplyPermissionAsync(AgentPermissionReplyRequest request, CancellationToken cancellationToken)
    {
        switch (request.ScopeKind)
        {
            case AgentEventScope.Thread:
                {
                    var accepted = _permissionGate.Reply(request.ScopeId, request.RequestId, request.Outcome);
                    if (accepted)
                    {
                        await EmitAsync(AgentEventScope.Thread, request.ScopeId, AgentEventKinds.Lifecycle,
                            $"Permission '{request.RequestId}' → {request.Outcome}", cancellationToken);
                        return new AgentControlResult(AgentControlStatus.Ok, Payload: new { outcome = request.Outcome });
                    }

                    return new AgentControlResult(AgentControlStatus.Gone, Error: "request-expired-or-unknown");
                }

            case AgentEventScope.Run when request.RequestId.StartsWith("stage:", StringComparison.Ordinal):
                {
                    var stageKey = request.RequestId["stage:".Length..];
                    if (await _pipelines.GetAsync(request.ScopeId, cancellationToken) is null)
                    {
                        return new AgentControlResult(AgentControlStatus.Conflict, Error: "no-such-run");
                    }

                    var exec = string.Equals(request.Outcome, "deny", StringComparison.OrdinalIgnoreCase)
                        ? await _pipelines.RejectStageAsync(request.ScopeId, stageKey, request.Comment, cancellationToken)
                        : await _pipelines.ApproveStageAsync(request.ScopeId, stageKey, request.Comment, cancellationToken);
                    await EmitAsync(AgentEventScope.Run, request.ScopeId, AgentEventKinds.Approval,
                        $"Approval '{request.RequestId}' → {request.Outcome}", cancellationToken);
                    return new AgentControlResult(AgentControlStatus.Ok, Payload: exec);
                }

            case AgentEventScope.Issue:
                return new AgentControlResult(AgentControlStatus.Conflict,
                    Error: "one-shot issue runs cannot receive permission replies.");

            default:
                return new AgentControlResult(AgentControlStatus.BadRequest,
                    Error: $"unknown scope '{request.ScopeKind}'.");
        }
    }

    /// <summary>State snapshot for UI mount/reconnect (RF-005).</summary>
    public async Task<AgentControlResult> GetScopeStateAsync(string scopeKind, string scopeId, CancellationToken cancellationToken)
    {
        var lastSeq = await _eventRepository.GetMaxSequenceAsync(scopeKind, scopeId, cancellationToken);

        switch (scopeKind)
        {
            case AgentEventScope.Run:
                {
                    var exec = await _pipelines.GetAsync(scopeId, cancellationToken);
                    if (exec is null)
                    {
                        return new AgentControlResult(AgentControlStatus.NotFound, Error: "no-such-run");
                    }

                    var state = exec.Status.ToLowerInvariant() switch
                    {
                        "running" or "inprogress" => "running",
                        "waitingapproval" => "waiting_permission",
                        "awaitingretry" => "awaiting_retry",
                        "paused" => "paused",
                        "completed" => "completed",
                        "failed" => "failed",
                        "cancelled" or "canceled" => "stopped",
                        _ => "queued",
                    };
                    return new AgentControlResult(AgentControlStatus.Ok,
                        Payload: new AgentScopeState(scopeKind, scopeId, state, LastEventSequence: lastSeq));
                }

            case AgentEventScope.Thread:
                {
                    var peer = _sessionClient.GetPeerInfo(scopeId);
                    var sessionInfo = peer is null
                        ? null
                        : JsonSerializer.Serialize(new
                        {
                            protocolVersion = peer.ProtocolVersion,
                            agent = new { name = peer.AgentName, version = peer.AgentVersion },
                            peer.Modes,
                            peer.ConfigOptions,
                            authMethods = peer.AuthMethods.Select(m => new { m.Id, m.Name, m.Type }),
                        });
                    return new AgentControlResult(AgentControlStatus.Ok, Payload: new AgentScopeState(
                        scopeKind, scopeId,
                        _sessionClient.IsSessionActive(scopeId) ? "running" : "idle",
                        LastEventSequence: lastSeq,
                        SessionId: peer?.SessionId,
                        SessionInfoJson: sessionInfo));
                }

            case AgentEventScope.Issue:
                {
                    var latest = (await _orchestration.GetRunsAsync(scopeId, 1, cancellationToken)).FirstOrDefault();
                    var issueState = latest?.State switch
                    {
                        AgentRunState.Running => "running",
                        AgentRunState.Queued => "queued",
                        AgentRunState.Succeeded => "completed",
                        AgentRunState.Failed => "failed",
                        AgentRunState.Canceled => "stopped",
                        _ => "idle",
                    };
                    return new AgentControlResult(AgentControlStatus.Ok,
                        Payload: new AgentScopeState(scopeKind, scopeId, issueState, LastEventSequence: lastSeq));
                }

            default:
                return new AgentControlResult(AgentControlStatus.BadRequest,
                    Error: "scopeKind must be run|thread|issue.");
        }
    }

    private Task EmitAsync(AgentControlRequest request, string kind, string title, CancellationToken ct) =>
        EmitAsync(request.ScopeKind, request.ScopeId, kind, title, ct, request.StageId);

    private Task EmitAsync(AgentControlRequest request, string kind, string title, string? payload, CancellationToken ct) =>
        EmitAsync(request.ScopeKind, request.ScopeId, kind, title, ct, request.StageId, payload);

    private Task EmitAsync(string scopeKind, string scopeId, string kind, string title, CancellationToken ct,
        string? stageId = null, string? payload = null) =>
        _sink.EmitAsync(new AgentExecutionEvent(
            string.Empty, scopeKind, scopeId, 0, DateTimeOffset.UtcNow,
            kind, stageId, Title: title, PayloadJson: payload), ct);
}
