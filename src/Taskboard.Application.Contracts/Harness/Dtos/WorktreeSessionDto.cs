namespace Taskboard.Dtos;

public sealed record WorktreeSessionDto(
    string WorktreeId,
    string RunId,
    string Path,
    string Branch,
    string Status,
    string RepositoryPath,
    string BaseBranch,
    string? CommitSha,
    bool RetainOnFailure,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    long Version);
