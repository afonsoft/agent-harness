using Taskboard.Agents;
using Taskboard.Harness;
using Taskboard.Harness.FinOps;

namespace Taskboard.Dtos;

/// <summary>
/// Structured cockpit event streamed over the `/harness-cockpit-hub` SignalR
/// group of a run (SPEC-20260919-ade-cockpit-hitl RF-001). `Kind` drives the
/// card rendered client-side: `stage`, `agent_output`, `verification`,
/// `approval`, `steer`, `diff`, `status`.
/// </summary>
public sealed record CockpitEventDto(
    string RunId,
    DateTimeOffset TimestampUtc,
    string Kind,
    string Title,
    string? PayloadJson);

/// <summary>
/// Interactive approval request pushed as `RequireApproval` — the client
/// answers via `POST /api/harness/runs/{runId}/approvals/{requestId}`.
/// </summary>
public sealed record ApprovalRequestDto(
    string RunId,
    string RequestId,
    string Title,
    string Description,
    IReadOnlyList<string> Options);

/// <summary>Body for `POST /api/harness/runs` — starts a pipeline execution (a "run").</summary>
public sealed record RunStartRequest(
    string TemplateId,
    string RepositoryFullName,
    string BaseBranch,
    string? IssueId,
    string? SpecPath,
    string Prompt,
    decimal? MaxBudgetUsd = null,
    /// <summary>`single-agent` only — which agent CLI runs the AgentWork stage (SPEC-20260920 R1).</summary>
    AgentType? AgentOverride = null,
    /// <summary>`single-agent` only — model tier for the AgentWork stage.</summary>
    AgentModelTier? TierOverride = null,
    /// <summary>`single-agent` only — drops the Verification stage when true.</summary>
    bool SkipVerification = false);

/// <summary>Body for `POST /api/harness/runs/{id}/steer` (RF-003).</summary>
public sealed record SteerRequest(string Instruction);

/// <summary>Body for `POST /api/harness/runs/{id}/approvals/{requestId}` (RF-004).</summary>
public sealed record ApprovalReplyRequest(string Action, string? Comment);

/// <summary>Body for `POST /api/harness/runs/{id}/create-pr` (RF-005).</summary>
public sealed record CreatePrRequest(string Title, string? Body);

/// <summary>
/// Run detail snapshot for `GET /api/harness/runs/{id}` — pipeline execution
/// plus telemetry (tokens/cost) and worktree info when present.
/// </summary>
public sealed record RunDetailsDto(
    PipelineExecutionDto Execution,
    RunTelemetryDto? Telemetry,
    WorktreeSessionDto? Worktree);
