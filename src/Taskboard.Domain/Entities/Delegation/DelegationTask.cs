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

    /// <summary>SPEC-20261020-v2 RF-001: coordination run this task belongs to.</summary>
    public string? RunId { get; private set; }

    /// <summary>SPEC-20261020-v2 RF-002: the in-flight dispatch attempt, when any.</summary>
    public string? ActiveDispatchId { get; private set; }

    public string Prompt { get; private set; } = string.Empty;

    /// <summary>AgentType name or custom-def id/display/executable.</summary>
    public string CliName { get; private set; } = string.Empty;

    /// <summary>Task ids (JSON) that must reach <c>done</c> first.</summary>
    public string DependsOnJson { get; private set; } = "[]";
    public string? RetryOf { get; private set; }
    public string? FanoutGroupId { get; private set; }

    /// <summary>SPEC-20261009 RF-001: <c>task</c> or <c>coordinate</c>.</summary>
    public string Kind { get; private set; } = DelegationTaskKinds.Task;

    public bool UseWorktree { get; private set; }

    /// <summary>Worktree session run-id when <see cref="UseWorktree"/> was honored.</summary>
    public string? WorktreeRunId { get; private set; }

    /// <summary>
    /// SPEC-20261020-worktree-shared-context RF-009: JSON list of shared
    /// paths provisioned into the worktree — echoed in the worker preamble.
    /// </summary>
    public string SharedPathsJson { get; private set; } = "[]";

    /// <summary>
    /// SPEC-20261021-worktree-setup-scripts RF-004: snapshot of the worktree
    /// setup outcome at attach — echoed in the worker preamble so the worker
    /// knows whether install steps already ran (<c>ok</c>), are still running
    /// (<c>running</c>), or failed.
    /// </summary>
    public string? SetupState { get; private set; }

    /// <summary>Scrubbed tail of the setup log (≤4KB) for the preamble.</summary>
    public string? SetupLogTail { get; private set; }

    public string WorkspacePath { get; private set; } = string.Empty;
    public string? RepositoryPath { get; private set; }

    /// <summary>HEAD sha of <see cref="RepositoryPath"/> at create — stale-base guard (RF-005).</summary>
    public string? BaseCommitSha { get; private set; }

    /// <summary>SPEC-20261020-v2 RF-008: requested launch model — adapter resolves the effective one.</summary>
    public string? Model { get; private set; }

    /// <summary>SPEC-20261020-v2 RF-008: requested launch effort (low|normal|high → tier).</summary>
    public string? Effort { get; private set; }

    public DelegationTaskStatus Status { get; private set; } = DelegationTaskStatus.Pending;
    public string? ResultSummary { get; private set; }
    public string? Error { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? FinishedAt { get; private set; }
    public DateTime? LastHeartbeatAt { get; private set; }

    public IReadOnlyList<string> DependsOn =>
        JsonSerializer.Deserialize<List<string>>(DependsOnJson) ?? [];

    /// <summary>Shared paths applied at worktree provisioning (RF-009).</summary>
    public IReadOnlyList<string> SharedPathsApplied =>
        SharedPathsJson is { Length: > 0 } json
            ? JsonSerializer.Deserialize<List<string>>(json) ?? []
            : [];

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
        string? kind = null,
        string? runId = null,
        string? model = null,
        string? effort = null,
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
            Kind = string.IsNullOrWhiteSpace(kind) ? DelegationTaskKinds.Task : kind.Trim(),
            UseWorktree = useWorktree,
            WorkspacePath = workspacePath,
            RepositoryPath = repositoryPath,
            BaseCommitSha = baseCommitSha,
            RunId = string.IsNullOrWhiteSpace(runId) ? null : runId.Trim(),
            Model = string.IsNullOrWhiteSpace(model) ? null : model.Trim(),
            Effort = string.IsNullOrWhiteSpace(effort) ? null : effort.Trim().ToLowerInvariant(),
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

    public void AttachWorktree(
        string runId,
        IReadOnlyList<string>? sharedPathsApplied = null,
        string? setupState = null,
        string? setupLogTail = null)
    {
        WorktreeRunId = runId;
        if (sharedPathsApplied is not null)
        {
            SharedPathsJson = JsonSerializer.Serialize(sharedPathsApplied);
        }

        SetupState = setupState;
        SetupLogTail = setupLogTail is { Length: > 4096 } tail
            ? tail[^4096..]
            : setupLogTail;
    }

    /// <summary>SPEC-20261020-v2 RF-002: point at the in-flight dispatch attempt.</summary>
    public void AttachDispatch(string dispatchId) =>
        ActiveDispatchId = string.IsNullOrWhiteSpace(dispatchId) ? ActiveDispatchId : dispatchId.Trim();

    /// <summary>Dispatch settled/abandoned — no in-flight attempt anymore.</summary>
    public void ClearDispatch() => ActiveDispatchId = null;

    /// <summary>
    /// SPEC-20261020-v2 RF-007: retry reactivates the same task — the new
    /// dispatch attempt is created by the service; deps re-evaluate from
    /// <c>pending</c> so a still-running dep keeps it waiting.
    /// </summary>
    public void ReactivateForRetry()
    {
        if (!IsRetryable)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"cannot retry a {Status.ToString().ToLowerInvariant()} task");
        }

        Status = DelegationTaskStatus.Pending;
        Error = null;
        ResultSummary = null;
        FinishedAt = null;
        LastHeartbeatAt = null;
        ActiveDispatchId = null;
    }

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

    /// <summary>
    /// SPEC-20261020-v2 RF-005/RF-010: non-terminal hold — a gate or an
    /// external update parks the task until unblocked; the DAG verdict
    /// re-evaluates it afterwards. Reason is stored structured.
    /// </summary>
    public void MarkBlocked(string? reason)
    {
        if (IsTerminal || Status is DelegationTaskStatus.Blocked)
        {
            return;
        }

        Status = DelegationTaskStatus.Blocked;
        ResultSummary = string.IsNullOrWhiteSpace(reason) ? ResultSummary : reason.Trim();
    }

    /// <summary>Gate resolved — back to pending so the deps verdict decides.</summary>
    public void Unblock()
    {
        if (Status is DelegationTaskStatus.Blocked)
        {
            Status = DelegationTaskStatus.Pending;
        }
    }

    public void Heartbeat(DateTime? now = null) => LastHeartbeatAt = now ?? DateTime.UtcNow;
}
