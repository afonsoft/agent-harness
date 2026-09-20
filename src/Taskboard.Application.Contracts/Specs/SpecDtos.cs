namespace Taskboard.Dtos;

/// <summary>SPEC-20260919-ade-living-specs §5 — catalog row for /specs.</summary>
public sealed record LivingSpecDto(
    string Id,
    string Title,
    string? Type,
    string Status,
    string? RawStatus,
    string? Date,
    string? Ticket,
    int RequirementsCount,
    int AcceptanceCriteriaCount,
    int TasksTotal,
    int TasksDone,
    IReadOnlyList<string> Warnings);

/// <summary>Full parsed spec, including sections for the viewer modal.</summary>
public sealed record LivingSpecDetailDto(
    string Id,
    string Title,
    string? Type,
    string Status,
    string? RawStatus,
    string? Date,
    string? Ticket,
    string? Branch,
    /// <summary>Repo root containing the spec file — target for "Run with Agent".</summary>
    string? RepositoryPath,
    IReadOnlyList<SpecRequirementDto> Requirements,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<SpecTaskDto> Tasks,
    IReadOnlyList<string> ReferencedFiles,
    IReadOnlyList<string> Warnings,
    string Markdown);

public sealed record SpecRequirementDto(string Code, string Title);

public sealed record SpecTaskDto(string Title, bool Done);

/// <summary>POST /api/specs/{id}/status body.</summary>
public sealed record SpecStatusUpdateRequest(string Status);

/// <summary>SPEC-20260919-ade-living-specs §5 — drift report payload.</summary>
public sealed record SpecDriftReportDto(
    int TotalSpecs,
    int StaleSpecsCount,
    IReadOnlyList<SpecDriftItemDto> DriftItems);

public sealed record SpecDriftItemDto(
    string SpecId,
    string CurrentStatus,
    string SuggestedStatus,
    string Reason,
    IReadOnlyList<string> MissingFiles);
