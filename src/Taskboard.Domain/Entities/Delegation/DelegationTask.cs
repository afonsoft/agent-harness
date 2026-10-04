using System.Text.Json;
using Taskboard;
using Taskboard.Delegation;

namespace Taskboard.Domain.Entities.Delegation;

/// <summary>
/// SPEC-20261005 RF-001: a unit of delegated work in the chat-side DAG.
/// <c>pending</c> waits on <see cref="DependsOn"/>; the dispatcher promotes to
/// <c>ready</c> when every dep is <see cref="DelegationTaskStatus.Done"/>, runs
/// it, and lands on <c>done</c>/<c>failed</c>/<c>stale</c>/<c>cancelled</c>.
/// </summary>
public sealed class DelegationTask : AggregateRoot<string>
{
    private DelegationTask(string id)
        : base(id)
    {
    }

    /// <summary>Mailbox/DAG scope — the owning conversation id, or "harness".</summary>
    public string Scope { get; private set; } = string.Empty;
    public string Prompt { get; private set; } = string.Empty;

    /// <summary>AgentType name or custom-def id/display/executable.</summary>
    public string CliName { get; private set; } = string.Empty;

    /// <summary>Task ids (JSON) that must reach <c>done</c> first.</summary>
    public string DependsOnJson { get; private set; } = "[]";
    public string? RetryOf { get; private set; }
    public string? FanoutGroupId { get; private set; }
    public bool UseWorktree { get; private set; }

    /// <summary>Worktree session run-id when <see cref="UseWorktree"/> was honored.</summary>
    public string? WorktreeRunId { get; private set; }

    public string WorkspacePath { get; private set; } = string.Empty;
    public string? RepositoryPath { get; private set; }

    /// <summary>HEAD sha of <see cref="RepositoryPath"/> at create — stale-base guard (RF-005).</summary>
    public string? BaseCommitSha { get; private set; }

    public DelegationTaskStatus Status { get; private set; } = DelegationTaskStatus.Pending;
    public string? ResultSummary { get; private set; }
    public string? Error { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? FinishedAt { get; private set; }
    public DateTime? LastHeartbeatAt { get; private set; }

    public IReadOnlyList<string> DependsOn =>
        JsonSerializer.Deserialize<List<string>>(DependsOnJson) ?? [];

    public static DelegationTask Create(
        string prompt,
        string cliName,
        string scope,
        string workspacePath,
        IReadOnlyList<string>? dependsOn = null,
        string? retryOf = null,
        string? fanoutGroupId = null,
        bool useWorktree = false,
        string? repositoryPath = null,
        string? baseCommitSha = null,
        DateTime? now = null)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "prompt is required");
        }

        if (string.IsNullOrWhiteSpace(cliName))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "cliName is required");
        }

        if (useWorktree && string.IsNullOrWhiteSpace(repositoryPath))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                "useWorktree requires a git repositoryPath");
        }

        return new DelegationTask($"task-{Guid.NewGuid():N}")
        {
            Scope = string.IsNullOrWhiteSpace(scope) ? "harness" : scope.Trim(),
            Prompt = prompt.Trim(),
            CliName = cliName.Trim(),
            DependsOnJson = JsonSerializer.Serialize(dependsOn ?? []),
            RetryOf = retryOf,
            FanoutGroupId = fanoutGroupId,
            UseWorktree = useWorktree,
            WorkspacePath = workspacePath,
            RepositoryPath = repositoryPath,
            BaseCommitSha = baseCommitSha,
            CreatedAt = now ?? DateTime.UtcNow,
        };
    }

    /// <summary>Terminal states — a finished task never runs again.</summary>
    public bool IsTerminal =>
        Status is DelegationTaskStatus.Done or DelegationTaskStatus.Failed
            or DelegationTaskStatus.Stale or DelegationTaskStatus.Cancelled;

    /// <summary>retry_of can only point at a terminal non-success task.</summary>
    public bool IsRetryable =>
        Status is DelegationTaskStatus.Failed or DelegationTaskStatus.Stale
            or DelegationTaskStatus.Cancelled;

    public void AttachWorktree(string runId) => WorktreeRunId = runId;

    public void MarkReady()
    {
        if (Status == DelegationTaskStatus.Pending)
        {
            Status = DelegationTaskStatus.Ready;
        }
    }

    public void MarkRunning(DateTime? now = null)
    {
        Status = DelegationTaskStatus.Running;
        StartedAt = now ?? DateTime.UtcNow;
        LastHeartbeatAt = StartedAt;
    }

    public void MarkDone(string? summary, DateTime? now = null)
    {
        Status = DelegationTaskStatus.Done;
        ResultSummary = summary;
        FinishedAt = now ?? DateTime.UtcNow;
    }

    public void MarkFailed(string error, DateTime? now = null)
    {
        Status = DelegationTaskStatus.Failed;
        Error = error;
        FinishedAt = now ?? DateTime.UtcNow;
    }

    public void MarkStale(string reason, DateTime? now = null)
    {
        Status = DelegationTaskStatus.Stale;
        Error = reason;
        FinishedAt = now ?? DateTime.UtcNow;
    }

    public void MarkCancelled(string reason, DateTime? now = null)
    {
        Status = DelegationTaskStatus.Cancelled;
        Error = reason;
        FinishedAt = now ?? DateTime.UtcNow;
    }

    public void Heartbeat(DateTime? now = null) => LastHeartbeatAt = now ?? DateTime.UtcNow;
}
