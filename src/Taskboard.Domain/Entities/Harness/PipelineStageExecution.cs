using Taskboard.Agents;
using Taskboard.Harness;

namespace Taskboard.Domain.Entities.Harness;

/// <summary>
/// Runtime state of one stage inside a <see cref="PipelineExecution"/> —
/// persisted so the pipeline survives restarts (SPEC-20260919-ade-multi-agent-orchestration §2).
/// </summary>
public sealed class PipelineStageExecution : Entity<PipelineStageExecutionId>
{
    public PipelineExecutionId ExecutionId { get; private set; } = default!;
    public string StageKey { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public PipelineStageKind Kind { get; private set; }
    public AgentRole? Role { get; private set; }
    public AgentType? Agent { get; private set; }
    public AgentModelTier ModelTier { get; private set; }
    public StageStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public IReadOnlyList<string> DependsOn { get; private set; } = [];
    public string? AdjustedPrompt { get; private set; }
    public string? HandoffSummary { get; private set; }
    public string? ApprovalComment { get; private set; }
    public string? LastError { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>
    /// CLIs already attempted for this stage, in order — persisted so a
    /// fallback chain never re-tries a CLI that already failed and survives
    /// restarts (SPEC-20260922-cockpit-agent-selection-fallback RF-003).
    /// </summary>
    public IReadOnlyList<string> TriedAgents { get; private set; } = [];

    /// <summary>
    /// Failures counted against the agent currently bound to the stage —
    /// reset on rotation or manual retry (SPEC-20260923-cockpit-run-hardening
    /// RF-002). Persisted so the schedule survives restarts.
    /// </summary>
    public int AutoRetryCount { get; private set; }

    /// <summary>Next due auto-retry; null means the failure is not yet scheduled.</summary>
    public DateTime? NextAutoRetryAtUtc { get; private set; }

    private PipelineStageExecution()
    {
    }

    internal static PipelineStageExecution FromDefinition(
        PipelineExecutionId executionId, PipelineStage stage)
    {
        return new PipelineStageExecution
        {
            Id = PipelineStageExecutionId.NewGuid(),
            ExecutionId = executionId,
            StageKey = stage.Key,
            Name = stage.Name,
            Kind = stage.Kind,
            Role = stage.Role,
            Agent = stage.Agent,
            ModelTier = stage.ModelTier,
            Status = StageStatus.Pending,
            Attempts = 1,
            DependsOn = stage.DependsOn,
        };
    }

    internal void MarkRunning(DateTime now)
    {
        if (Status is not StageStatus.Pending)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Stage '{StageKey}' cannot start from {Status}.");
        }

        Status = StageStatus.Running;
        StartedAtUtc = now;
        LastError = null;
    }

    internal void MarkWaitingApproval()
    {
        Status = StageStatus.WaitingApproval;
    }

    internal void Complete(string? handoffSummary, DateTime now)
    {
        if (Status is not (StageStatus.Running or StageStatus.WaitingApproval))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Stage '{StageKey}' cannot complete from {Status}.");
        }

        Status = StageStatus.Completed;
        HandoffSummary = handoffSummary;
        CompletedAtUtc = now;
    }

    internal void Approve(string? comment, DateTime now)
    {
        if (Status is not StageStatus.WaitingApproval)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Stage '{StageKey}' is not waiting for approval.");
        }

        Status = StageStatus.Completed;
        ApprovalComment = comment;
        CompletedAtUtc = now;
    }

    internal void Fail(string error, DateTime now)
    {
        Status = StageStatus.Failed;
        LastError = error;
        CompletedAtUtc = now;
    }

    internal void Retry(string? adjustedPrompt)
    {
        if (Status is not StageStatus.Failed)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Stage '{StageKey}' cannot retry from {Status}.");
        }

        Status = StageStatus.Pending;
        Attempts++;
        AdjustedPrompt = adjustedPrompt;
        LastError = null;
        StartedAtUtc = null;
        CompletedAtUtc = null;
        // Manual retry re-opens the whole fallback chain and the auto-retry
        // budget (SPEC-20260922 §6 + SPEC-20260923 RF-002).
        TriedAgents = [];
        AutoRetryCount = 0;
        NextAutoRetryAtUtc = null;
    }

    /// <summary>
    /// Counts one more failure against the bound agent and schedules the next
    /// auto-retry tick (RF-002). The stage stays Failed until the due time.
    /// </summary>
    internal void ScheduleAutoRetry(DateTime retryAtUtc)
    {
        if (Status is not StageStatus.Failed)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Stage '{StageKey}' cannot schedule an auto-retry from {Status}.");
        }

        AutoRetryCount++;
        NextAutoRetryAtUtc = retryAtUtc;
    }

    /// <summary>
    /// Rotates the stage to the next untried CLI after the current agent
    /// exhausted its budget — the new agent's first retry is scheduled at
    /// <paramref name="retryAtUtc"/> (RF-002).
    /// </summary>
    internal void RotateAgent(AgentType next, DateTime retryAtUtc)
    {
        if (Status is not StageStatus.Failed)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Stage '{StageKey}' cannot rotate agents from {Status}.");
        }

        Agent = next;
        AutoRetryCount = 0;
        NextAutoRetryAtUtc = retryAtUtc;
    }

    /// <summary>
    /// Due auto-retry: back to Pending on the bound agent — the dispatch loop
    /// picks it up in the same tick. <see cref="TriedAgents"/> is kept so the
    /// rotation order is preserved (RF-002).
    /// </summary>
    internal void BeginAutoRetry()
    {
        if (Status is not StageStatus.Failed || NextAutoRetryAtUtc is null)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Stage '{StageKey}' has no scheduled auto-retry (status {Status}).");
        }

        Status = StageStatus.Pending;
        NextAutoRetryAtUtc = null;
        Attempts++;
        StartedAtUtc = null;
        CompletedAtUtc = null;
    }

    /// <summary>
    /// Marks the current agent as tried and keeps the stage Running — the
    /// engine immediately dispatches the next fallback candidate
    /// (SPEC-20260922-cockpit-agent-selection-fallback RF-003).
    /// </summary>
    internal void RecordAttemptFailure(string error)
    {
        if (Status is not StageStatus.Running)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Stage '{StageKey}' cannot record an attempt failure from {Status}.");
        }

        if (Agent is { } agent)
        {
            var name = agent.ToString();
            if (!TriedAgents.Contains(name, StringComparer.Ordinal))
            {
                TriedAgents = [.. TriedAgents, name];
            }
        }

        LastError = error;
    }

    /// <summary>Switches the stage to the next fallback CLI and counts the attempt.</summary>
    internal void BeginFallbackAttempt(AgentType next)
    {
        if (Status is not StageStatus.Running)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Stage '{StageKey}' cannot fall back from {Status}.");
        }

        Agent = next;
        Attempts++;
        LastError = null;
    }

    internal void Skip()
    {
        if (Status is StageStatus.Pending or StageStatus.Running or StageStatus.WaitingApproval)
        {
            Status = StageStatus.Skipped;
        }
    }
}
