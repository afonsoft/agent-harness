using Taskboard.Harness;

namespace Taskboard.Application.Contracts.Harness;

/// <summary>
/// SPEC-20261020-worktree-shared-context RF-006: card annotation bound to a
/// worktree — comment + workspace cleanliness shown on the board/detail.
/// </summary>
public sealed record WorktreeCardDto(
    string WorktreePath,
    string RepoPath,
    string? Comment,
    WorkspaceStatus WorkspaceStatus,
    string? TaskId,
    DateTime StatusCommentedAtUtc,
    DateTime UpdatedAt);

/// <summary>Persistence for <see cref="WorktreeCardDto"/> annotations.</summary>
public interface IWorktreeCardRepository
{
    /// <summary>Upserts by <see cref="WorktreeCardDto.WorktreePath"/> and
    /// returns the stored row.</summary>
    Task<WorktreeCardDto> UpsertAsync(
        string worktreePath,
        string repoPath,
        string? comment,
        WorkspaceStatus status,
        string? taskId,
        CancellationToken cancellationToken = default);

    Task<WorktreeCardDto?> GetAsync(
        string worktreePath, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorktreeCardDto>> ListAsync(
        string? repoPath = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Derives a worktree's workspace state on demand
/// (<c>git status --porcelain</c>) — Integrations provides the git-backed
/// implementation; a missing/failed probe yields
/// <see cref="WorkspaceStatus.Unknown"/>.
/// </summary>
public interface IWorktreeStatusProbe
{
    Task<WorkspaceStatus> ProbeAsync(
        string worktreePath, CancellationToken cancellationToken = default);

    /// <summary>Main repository root owning the worktree; null when the path
    /// is not a git checkout.</summary>
    Task<string?> ResolveRepoPathAsync(
        string worktreePath, CancellationToken cancellationToken = default);
}

/// <summary>Card writes + reads; status <c>unknown</c>/null derives on write.</summary>
public interface IWorktreeCardService
{
    /// <summary>
    /// Sets comment/status on a worktree. <paramref name="status"/> accepts
    /// <c>clean|dirty|conflict|unknown</c> (case-insensitive); null or
    /// <c>unknown</c> probes git on demand. Emits a <c>worktree.card</c> push.
    /// </summary>
    Task<WorktreeCardDto> SetCardAsync(
        string worktreePath,
        string? comment,
        string? status,
        string? taskId = null,
        CancellationToken cancellationToken = default);

    Task<WorktreeCardDto?> GetAsync(
        string worktreePath, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorktreeCardDto>> ListAsync(
        string? repoPath = null, CancellationToken cancellationToken = default);
}

/// <summary>Delivers <c>worktree.card</c> patches to connected clients.</summary>
public interface IWorktreeCardNotifier
{
    Task PushAsync(WorktreeCardDto card, CancellationToken ct = default);
}
