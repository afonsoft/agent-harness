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

    /// <summary>
    /// SPEC-20261020-worktree-shared-context RF-009: JSON list of shared
    /// paths materialized at provisioning — the worker preamble echoes it.
    /// </summary>
    public string SharedPathsJson { get; private set; } = "[]";

    /// <summary>
    /// SPEC-20261021-worktree-setup-scripts RF-003: setup outcome snapshot at
    /// provisioning — <c>ok|failed|running|skipped</c> (null = no setup ran).
    /// </summary>
    public string? SetupState { get; private set; }

    /// <summary>Scrubbed tail of the setup log (≤4KB).</summary>
    public string? SetupLogTail { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private WorktreeSession()
    {
    }

    private WorktreeSession(
        WorktreeSessionId id,
        string runId,
        WorktreeSessionPaths paths,
        bool retainOnFailure,
        DateTime now,
        WorktreeProvisionOutcome? provisioning = null)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "RunId cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(paths.RepositoryPath))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "RepositoryPath cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(paths.BaseBranch))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "BaseBranch cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(paths.Path))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Path cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(paths.Branch))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Branch cannot be empty.");
        }

        RunId = runId;
        RepositoryPath = paths.RepositoryPath;
        BaseBranch = paths.BaseBranch;
        Path = paths.Path;
        Branch = paths.Branch;
        Status = WorktreeStatus.Active;
        RetainOnFailure = retainOnFailure;
        ApplyProvisioning(provisioning);
        CreatedAt = UpdatedAt = now;
    }

    public static WorktreeSession Create(
        WorktreeSessionId id,
        string runId,
        WorktreeSessionPaths paths,
        bool retainOnFailure = false,
        DateTime? now = null,
        WorktreeProvisionOutcome? provisioning = null)
        => new(id, runId, paths, retainOnFailure,
            now ?? DateTime.UtcNow, provisioning);

    /// <summary>
    /// Reactivates the session for a retried run whose previous worktree
    /// directory was lost — keeps the row (RunId is unique) and clears the
    /// recorded commit.
    /// </summary>
    public void Reactivate(
        WorktreeSessionPaths paths,
        bool retainOnFailure,
        DateTime? now = null,
        WorktreeProvisionOutcome? provisioning = null)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseBranch)
            || string.IsNullOrWhiteSpace(paths.Path)
            || string.IsNullOrWhiteSpace(paths.Branch))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "BaseBranch, Path and Branch cannot be empty.");
        }

        BaseBranch = paths.BaseBranch;
        Path = paths.Path;
        Branch = paths.Branch;
        RetainOnFailure = retainOnFailure;
        CommitSha = null;
        ApplyProvisioning(provisioning);
        Status = WorktreeStatus.Active;
        Touch(now);
    }

    private void ApplyProvisioning(WorktreeProvisionOutcome? provisioning)
    {
        SharedPathsJson = System.Text.Json.JsonSerializer.Serialize(
            provisioning?.SharedPathsApplied ?? []);
        SetupState = provisioning?.SetupState;
        SetupLogTail = provisioning?.SetupLogTail;
    }

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
