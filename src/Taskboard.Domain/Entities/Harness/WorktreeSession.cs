using Taskboard.Harness;

namespace Taskboard.Domain.Entities.Harness;

/// <summary>
/// Persisted record of an isolated Git worktree bound to an agent run
/// (SPEC-20260919-harness-workspace-isolation). Metadata only — never holds
/// instructions, credentials or diff payloads.
/// </summary>
public sealed class WorktreeSession : AggregateRoot<WorktreeSessionId>
{
    public string RunId { get; private set; } = default!;
    public string RepositoryPath { get; private set; } = default!;
    public string BaseBranch { get; private set; } = default!;
    public string Path { get; private set; } = default!;
    public string Branch { get; private set; } = default!;
    public WorktreeStatus Status { get; private set; }
    public string? CommitSha { get; private set; }
    public bool RetainOnFailure { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private WorktreeSession()
    {
    }

    private WorktreeSession(
        WorktreeSessionId id,
        string runId,
        string repositoryPath,
        string baseBranch,
        string path,
        string branch,
        bool retainOnFailure,
        DateTime now)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "RunId cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "RepositoryPath cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(baseBranch))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "BaseBranch cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Path cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(branch))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Branch cannot be empty.");
        }

        RunId = runId;
        RepositoryPath = repositoryPath;
        BaseBranch = baseBranch;
        Path = path;
        Branch = branch;
        Status = WorktreeStatus.Active;
        RetainOnFailure = retainOnFailure;
        CreatedAt = UpdatedAt = now;
    }

    public static WorktreeSession Create(
        WorktreeSessionId id,
        string runId,
        string repositoryPath,
        string baseBranch,
        string path,
        string branch,
        bool retainOnFailure = false,
        DateTime? now = null)
        => new(id, runId, repositoryPath, baseBranch, path, branch, retainOnFailure, now ?? DateTime.UtcNow);

    public void RecordCommit(string commitSha, DateTime? now = null)
    {
        EnsureNotRemoved();
        if (string.IsNullOrWhiteSpace(commitSha))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "CommitSha cannot be empty.");
        }

        CommitSha = commitSha;
        Touch(now);
    }

    public void MarkCompleted(DateTime? now = null)
    {
        EnsureNotRemoved();
        Status = WorktreeStatus.Completed;
        Touch(now);
    }

    public void MarkFailed(DateTime? now = null)
    {
        EnsureNotRemoved();
        Status = WorktreeStatus.Failed;
        Touch(now);
    }

    public void MarkRetainedForInspection(DateTime? now = null)
    {
        EnsureNotRemoved();
        if (Status != WorktreeStatus.Failed)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                "Only a failed worktree session can be retained for inspection.");
        }

        Status = WorktreeStatus.RetainedForInspection;
        Touch(now);
    }

    public void MarkRemoved(DateTime? now = null)
    {
        EnsureNotRemoved();
        Status = WorktreeStatus.Removed;
        Touch(now);
    }

    private void EnsureNotRemoved()
    {
        if (Status == WorktreeStatus.Removed)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                "Worktree session is already removed.");
        }
    }

    private void Touch(DateTime? now)
    {
        UpdatedAt = now ?? DateTime.UtcNow;
        IncrementVersion();
    }
}
