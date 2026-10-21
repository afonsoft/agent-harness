using Taskboard.Harness;

namespace Taskboard.Domain.Entities.Harness;

/// <summary>
/// SPEC-20261020-worktree-shared-context RF-006: human-facing annotation on a
/// worktree — why it exists and its workspace cleanliness. Written by the
/// <c>worktree_set_status</c> tool or the card endpoint; rendered on the
/// KanbanBoard worktree card (P3). Metadata only — never holds diffs or
/// credentials.
/// </summary>
public sealed class WorktreeCard : Entity<Guid>
{
    /// <summary>Max comment length (RF-006).</summary>
    public const int CommentMaxLength = 500;

    /// <summary>Absolute worktree path — natural key (unique index).</summary>
    public string WorktreePath { get; private set; } = default!;

    /// <summary>Main repository root the worktree belongs to.</summary>
    public string RepoPath { get; private set; } = default!;

    /// <summary>Free-text status comment (≤500 chars); null clears it.</summary>
    public string? Comment { get; private set; }

    /// <summary>Cleanliness of the checkout — explicit or porcelain-derived.</summary>
    public WorkspaceStatus WorkspaceStatus { get; private set; }

    /// <summary>Delegation task that owns the worktree, when known.</summary>
    public string? TaskId { get; private set; }

    /// <summary>Last time comment/status was written (UTC).</summary>
    public DateTime StatusCommentedAtUtc { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private WorktreeCard()
    {
    }

    public static WorktreeCard Create(
        string worktreePath, string repoPath, string? comment,
        WorkspaceStatus status, string? taskId, DateTime now)
    {
        var card = new WorktreeCard
        {
            Id = Guid.NewGuid(),
            WorktreePath = RequirePath(worktreePath, nameof(worktreePath)),
            RepoPath = RequirePath(repoPath, nameof(repoPath)),
            CreatedAt = now,
            UpdatedAt = now,
        };
        card.SetStatus(comment, status, taskId, now);
        return card;
    }

    /// <summary>Rewrites comment/status/taskId — the card is a single mutable
    /// annotation row, not an event log.</summary>
    public void SetStatus(
        string? comment, WorkspaceStatus status, string? taskId, DateTime now)
    {
        Comment = NormalizeComment(comment);
        WorkspaceStatus = status;
        TaskId = taskId ?? TaskId;
        StatusCommentedAtUtc = UpdatedAt = now;
    }

    private static string? NormalizeComment(string? comment)
    {
        var trimmed = comment?.Trim();
        if (trimmed is { Length: > CommentMaxLength })
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Comment must be ≤{CommentMaxLength} chars.");
        }

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string RequirePath(string path, string name)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, $"{name} is required.");
        }

        return path.Trim();
    }
}
