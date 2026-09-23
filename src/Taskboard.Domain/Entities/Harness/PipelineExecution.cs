using Taskboard.Agents;
using Taskboard.Harness;

namespace Taskboard.Domain.Entities.Harness;

/// <summary>
/// A running (or finished) pipeline — stages share one run worktree and hand
/// context forward as each completes (SPEC-20260919-ade-multi-agent-orchestration).
/// </summary>
public sealed class PipelineExecution : AggregateRoot<PipelineExecutionId>
{
    private readonly List<PipelineStageExecution> _stages = [];

    public string TemplateId { get; private set; } = default!;
    public string RepositoryFullName { get; private set; } = default!;
    public string RepositoryPath { get; private set; } = default!;
    public string BaseBranch { get; private set; } = default!;
    public string? IssueId { get; private set; }
    public string InitialPrompt { get; private set; } = default!;
    public string? WorktreePath { get; private set; }

    /// <summary>Budget cap in USD — cumulative stage cost above this cancels the execution (E14 RF-003).</summary>
    public decimal? BudgetCapUsd { get; private set; }
    public PipelineStatus Status { get; private set; }

    /// <summary>
    /// Why the run ended in <see cref="PipelineStatus.Failed"/> — stage key,
    /// tried agents and the last error
    /// (SPEC-20260923-cockpit-run-hardening RF-002).
    /// </summary>
    public string? FailureReason { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    public IReadOnlyList<PipelineStageExecution> Stages => _stages;

    private PipelineExecution()
    {
    }

    private PipelineExecution(
        PipelineExecutionId id,
        PipelineDefinition definition,
        string repositoryFullName,
        string repositoryPath,
        string baseBranch,
        string? issueId,
        string initialPrompt,
        decimal? budgetCapUsd,
        DateTime now)
        : base(id)
    {
        TemplateId = definition.TemplateId;
        RepositoryFullName = repositoryFullName;
        RepositoryPath = repositoryPath;
        BaseBranch = baseBranch;
        IssueId = issueId;
        InitialPrompt = initialPrompt;
        BudgetCapUsd = budgetCapUsd;
        Status = PipelineStatus.Running;
        CreatedAtUtc = now;
        foreach (var stage in definition.Stages)
        {
            _stages.Add(PipelineStageExecution.FromDefinition(id, stage));
        }
    }

    public static PipelineExecution Create(
        PipelineDefinition definition,
        string repositoryFullName,
        string repositoryPath,
        string baseBranch,
        string? issueId,
        string initialPrompt,
        DateTime now,
        decimal? budgetCapUsd = null)
    {
        definition.Validate();
        if (string.IsNullOrWhiteSpace(repositoryFullName))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "RepositoryFullName cannot be empty.");
        }
        if (string.IsNullOrWhiteSpace(initialPrompt))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "InitialPrompt cannot be empty.");
        }

        return new PipelineExecution(
            PipelineExecutionId.NewGuid(), definition,
            repositoryFullName, repositoryPath, baseBranch, issueId, initialPrompt, budgetCapUsd, now);
    }

    /// <summary>Pending stages whose `DependsOn` are all completed (RF-002).</summary>
    public IReadOnlyList<PipelineStageExecution> EligibleStages()
    {
        if (Status is not PipelineStatus.Running)
        {
            return [];
        }

        var completed = _stages
            .Where(s => s.Status is StageStatus.Completed)
            .Select(s => s.StageKey)
            .ToHashSet(StringComparer.Ordinal);
        return _stages
            .Where(s => s.Status is StageStatus.Pending && s.DependsOn.All(completed.Contains))
            .ToList();
    }

    public void AttachWorktree(string worktreePath)
    {
        if (string.IsNullOrWhiteSpace(worktreePath))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "WorktreePath cannot be empty.");
        }

        WorktreePath = worktreePath;
    }

    public void MarkStageRunning(string stageKey, DateTime now)
    {
        var stage = RequireStage(stageKey);
        if (!EligibleStages().Any(s => s.StageKey == stageKey))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Stage '{stageKey}' is not eligible to run.");
        }

        stage.MarkRunning(now);
    }

    /// <summary>Approval stages transition to WaitingApproval instead of Running.</summary>
    public void MarkStageWaitingApproval(string stageKey)
    {
        var stage = RequireStage(stageKey);
        stage.MarkWaitingApproval();
        RecomputeStatus();
    }

    public void CompleteStage(string stageKey, string? handoffSummary, DateTime now)
    {
        RequireStage(stageKey).Complete(handoffSummary, now);
        RecomputeStatus(now);
    }

    public void ApproveStage(string stageKey, string? comment, DateTime now)
    {
        RequireStage(stageKey).Approve(comment, now);
        RecomputeStatus(now);
    }

    public void FailStage(string stageKey, string error, DateTime now)
    {
        RequireStage(stageKey).Fail(error, now);
        RecomputeStatus(now);
    }

    /// <summary>Failed stage goes back to Pending with an optional corrected prompt (RF-005).</summary>
    public void RetryStage(string stageKey, string? adjustedPrompt, DateTime now)
    {
        RequireStage(stageKey).Retry(adjustedPrompt);
        RecomputeStatus(now);
    }

    /// <summary>
    /// Marks the running stage's current CLI as tried after a failed attempt
    /// (SPEC-20260922-cockpit-agent-selection-fallback RF-003).
    /// </summary>
    public void RecordStageAttemptFailure(string stageKey, string error)
    {
        RequireStage(stageKey).RecordAttemptFailure(error);
    }

    /// <summary>Switches the running stage to the next fallback CLI.</summary>
    public void BeginStageFallback(string stageKey, AgentType next)
    {
        RequireStage(stageKey).BeginFallbackAttempt(next);
    }

    /// <summary>
    /// Schedules the next auto-retry of a failed stage — counts the failure
    /// against the bound agent (SPEC-20260923-cockpit-run-hardening RF-002).
    /// </summary>
    public void ScheduleStageAutoRetry(string stageKey, DateTime retryAtUtc) =>
        RequireStage(stageKey).ScheduleAutoRetry(retryAtUtc);

    /// <summary>Rotates a failed stage to the next untried CLI and schedules its first retry.</summary>
    public void RotateStageAgent(string stageKey, AgentType next, DateTime retryAtUtc) =>
        RequireStage(stageKey).RotateAgent(next, retryAtUtc);

    /// <summary>Fires a due auto-retry — the stage goes back to Pending for this tick's dispatch.</summary>
    public void BeginStageAutoRetry(string stageKey, DateTime now)
    {
        RequireStage(stageKey).BeginAutoRetry();
        RecomputeStatus(now);
    }

    /// <summary>
    /// Terminal failure — the auto-retry budget was exhausted across every
    /// eligible agent CLI (RF-002). Pending/waiting stages are skipped; the
    /// failing stage keeps its <c>Failed</c> status and error detail.
    /// </summary>
    public void Fail(string reason, DateTime now)
    {
        if (Status is PipelineStatus.Completed or PipelineStatus.Cancelled or PipelineStatus.Failed)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidPipelineState,
                $"Pipeline cannot fail from {Status}.");
        }

        foreach (var stage in _stages)
        {
            stage.Skip();
        }

        Status = PipelineStatus.Failed;
        FailureReason = reason;
        CompletedAtUtc = now;
    }

    /// <summary>
    /// Operator pause — freezes the DAG between stages: in-flight stages still
    /// complete, but nothing new is dispatched until <see cref="Resume"/>
    /// (SPEC-20260920-cockpit-pause-resume AC1).
    /// </summary>
    public void Pause()
    {
        if (Status is not (PipelineStatus.Running or PipelineStatus.WaitingApproval or PipelineStatus.AwaitingRetry))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidPipelineState,
                $"Pipeline cannot pause from {Status}.");
        }

        Status = PipelineStatus.Paused;
    }

    /// <summary>Resumes a paused execution — the next tick dispatches eligible stages (AC2).</summary>
    public void Resume()
    {
        if (Status is not PipelineStatus.Paused)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidPipelineState,
                $"Pipeline cannot resume from {Status}.");
        }

        Status = PipelineStatus.Running;
        RecomputeStatus();
    }

    public void Cancel(DateTime now)
    {
        if (Status is PipelineStatus.Completed or PipelineStatus.Cancelled or PipelineStatus.Failed)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Pipeline cannot cancel from {Status}.");
        }

        foreach (var stage in _stages)
        {
            stage.Skip();
        }

        Status = PipelineStatus.Cancelled;
        CompletedAtUtc = now;
    }

    private PipelineStageExecution RequireStage(string stageKey) =>
        _stages.FirstOrDefault(s => s.StageKey == stageKey)
        ?? throw new DomainException(
            TaskboardDomainErrorCodes.InvalidValue, $"Unknown stage '{stageKey}'.");

    private void RecomputeStatus(DateTime? now = null)
    {
        if (Status is PipelineStatus.Completed or PipelineStatus.Cancelled or PipelineStatus.Paused
            or PipelineStatus.Failed)
        {
            return;
        }

        if (_stages.Any(s => s.Status is StageStatus.Failed))
        {
            Status = PipelineStatus.AwaitingRetry;
        }
        else if (_stages.All(s => s.Status is StageStatus.Completed or StageStatus.Skipped))
        {
            Status = PipelineStatus.Completed;
            CompletedAtUtc = now ?? DateTime.UtcNow;
        }
        else if (_stages.Any(s => s.Status is StageStatus.WaitingApproval))
        {
            Status = PipelineStatus.WaitingApproval;
        }
        else
        {
            Status = PipelineStatus.Running;
        }
    }
}
