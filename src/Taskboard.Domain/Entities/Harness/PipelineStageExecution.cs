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
    }

    internal void Skip()
    {
        if (Status is StageStatus.Pending or StageStatus.Running or StageStatus.WaitingApproval)
        {
            Status = StageStatus.Skipped;
        }
    }
}
