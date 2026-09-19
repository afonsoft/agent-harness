namespace Taskboard.Dtos;

/// <summary>
/// Payload for <c>POST /api/harness/worktrees</c>
/// (SPEC-20260919-harness-workspace-isolation §5).
/// </summary>
public sealed record CreateWorktreeRequestDto(
    string RunId,
    string RepositoryPath,
    string? BaseBranch,
    string TaskSlug,
    bool RetainOnFailure = false);
