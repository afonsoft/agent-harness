using Taskboard.Agents;
using Taskboard.Harness;

namespace Taskboard.Dtos;

/// <summary>Pipeline template metadata for pickers/UI.</summary>
public sealed record PipelineTemplateDto(
    string TemplateId,
    string Name,
    IReadOnlyList<string> StageKeys,
    /// <summary>
    /// Per-stage detail for the New Run stage grid — kind/role plus the
    /// template's default agent+tier so the UI never hard-codes template
    /// internals (SPEC-20260922-cockpit-agent-selection-fallback RF-001).
    /// </summary>
    IReadOnlyList<PipelineTemplateStageDto> Stages);

/// <summary>One stage of a pipeline template (defaults the UI can override).</summary>
public sealed record PipelineTemplateStageDto(
    string Key,
    string Name,
    string Kind,
    string? Role,
    string? DefaultAgent,
    string DefaultTier);

/// <summary>
/// Per-stage agent pick for `POST /api/harness/pipelines/start` — `null`
/// Agent means Auto (server resolves against eligibility)
/// (SPEC-20260922-cockpit-agent-selection-fallback RF-001).
/// </summary>
public sealed record PipelineStageOverrideDto(AgentType? Agent, AgentModelTier? Tier);

/// <summary>Request body for `POST /api/harness/pipelines/start`.</summary>
public sealed record PipelineStartRequest(
    string TemplateId,
    string RepositoryFullName,
    string RepositoryPath,
    string BaseBranch,
    string? IssueId,
    string InitialPrompt,
    /// <summary>Budget cap in USD — cumulative stage cost above this cancels the execution (E14 RF-003).</summary>
    decimal? MaxBudgetUsd = null,
    /// <summary>`single-agent` only — replaces the AgentWork stage's agent (SPEC-20260920 R1).</summary>
    AgentType? AgentOverride = null,
    /// <summary>`single-agent` only — replaces the AgentWork stage's model tier.</summary>
    AgentModelTier? TierOverride = null,
    /// <summary>`single-agent` only — drops the Verification stage when true.</summary>
    bool SkipVerification = false,
    /// <summary>
    /// Per-stage agent/tier picks keyed by stage key — any template
    /// (SPEC-20260922 RF-001). `null` Agent = Auto resolution.
    /// </summary>
    IReadOnlyDictionary<string, PipelineStageOverrideDto>? StageOverrides = null,
    /// <summary>
    /// Run every AgentWork stage on one CLI (SPEC-20260922 RF-005). When true
    /// and <see cref="SingleAgentType"/> is null, the server picks the first
    /// eligible CLI once and applies it to all stages.
    /// </summary>
    bool SingleAgent = false,
    /// <summary>The single CLI for the run, or null for Auto.</summary>
    AgentType? SingleAgentType = null,
    /// <summary>Tier applied to every AgentWork stage, or null to keep each stage's default.</summary>
    AgentModelTier? SingleAgentTier = null);

/// <summary>Body for `POST .../stages/{stageKey}/approve`.</summary>
public sealed record PipelineApproveRequest(string? Comment);

/// <summary>Body for `POST .../stages/{stageKey}/retry`.</summary>
public sealed record PipelineRetryRequest(string? AdjustedPrompt);

/// <summary>One stage row inside a pipeline execution DTO.</summary>
public sealed record PipelineStageDto(
    string StageKey,
    string Name,
    string Kind,
    string Status,
    string? Role,
    string? Agent,
    int Attempts,
    string? HandoffSummary,
    string? LastError,
    IReadOnlyList<string> DependsOn,
    /// <summary>
    /// CLIs already attempted for this stage, in order — the fallback chain
    /// (SPEC-20260922 RF-003/RF-006). `Agent` is the effective/last pick.
    /// </summary>
    IReadOnlyList<string>? TriedAgents = null);

/// <summary>Pipeline execution snapshot for status/list endpoints.</summary>
public sealed record PipelineExecutionDto(
    string PipelineExecutionId,
    string TemplateId,
    string RepositoryFullName,
    string BaseBranch,
    string Status,
    string? WorktreePath,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    IReadOnlyList<PipelineStageDto> Stages,
    /// <summary>Board issue this run was started for, when launched from the Kanban board.</summary>
    string? IssueId = null);
