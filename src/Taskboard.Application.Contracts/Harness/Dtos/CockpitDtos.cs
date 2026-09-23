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
/// `Summary` (SPEC-20260923-cockpit-run-hardening RF-005) is a one-line
/// digest — repo, run short id, stage and handoff — used by toasts and
/// browser notifications so the operator can decide without opening the run.
/// </summary>
public sealed record ApprovalRequestDto(
    string RunId,
    string RequestId,
    string Title,
    string Description,
    IReadOnlyList<string> Options,
    string? Summary = null);

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
    bool SkipVerification = false,
    /// <summary>Per-stage agent/tier picks keyed by stage key — any template (SPEC-20260922 RF-001).</summary>
    IReadOnlyDictionary<string, PipelineStageOverrideDto>? StageOverrides = null,
    /// <summary>Run every AgentWork stage on one CLI (SPEC-20260922 RF-005).</summary>
    bool SingleAgent = false,
    /// <summary>The single CLI for the run, or null for Auto.</summary>
    AgentType? SingleAgentType = null,
    /// <summary>Tier applied to every AgentWork stage, or null to keep stage defaults.</summary>
    AgentModelTier? SingleAgentTier = null);

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

/// <summary>
/// `GET /api/harness/runs/{id}/events` envelope (SPEC-20260923 RF-004): a
/// page of cockpit events over the durable normalized stream, plus the
/// cursor for the next poll (`after = nextAfter`).
/// </summary>
public sealed record CockpitEventsPage(
    IReadOnlyList<CockpitEventDto> Events,
    long NextAfter,
    bool HasMore);
