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
    string InitialPrompt);

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
    IReadOnlyList<PipelineStageDto> Stages);
