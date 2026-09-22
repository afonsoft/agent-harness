using Taskboard.Agents;
using Taskboard.Harness;

namespace Taskboard.Dtos;

/// <summary>Pipeline template metadata for pickers/UI.</summary>
public sealed record PipelineTemplateDto(
    string TemplateId,
    string Name,
    IReadOnlyList<string> StageKeys);

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
    bool SkipVerification = false);

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
    IReadOnlyList<string> DependsOn);

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
