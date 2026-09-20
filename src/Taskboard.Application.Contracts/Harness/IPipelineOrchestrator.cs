using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.Harness;

/// <summary>
/// Drives a DAG of specialized agent stages over a shared run worktree
/// (SPEC-20260919-ade-multi-agent-orchestration).
/// </summary>
public interface IPipelineOrchestrator
{
    /// <summary>Pre-configured pipeline templates (RF-001).</summary>
    Task<IReadOnlyList<PipelineTemplateDto>> ListTemplatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates the execution and dispatches eligible stages.</summary>
    Task<PipelineExecutionDto> StartAsync(PipelineStartRequest request, CancellationToken cancellationToken = default);

    /// <summary>Snapshot of a running/finished pipeline.</summary>
    Task<PipelineExecutionDto?> GetAsync(string pipelineExecutionId, CancellationToken cancellationToken = default);

    /// <summary>Approves a `WaitingApproval` stage; dependents become eligible (RF gates).</summary>
    Task<PipelineExecutionDto> ApproveStageAsync(
        string pipelineExecutionId, string stageKey, string? comment, CancellationToken cancellationToken = default);

    /// <summary>Re-queues a failed stage with an optional corrected prompt (RF-005).</summary>
    Task<PipelineExecutionDto> RetryStageAsync(
        string pipelineExecutionId, string stageKey, string? adjustedPrompt, CancellationToken cancellationToken = default);

    /// <summary>Safe shutdown of running stages; pipeline → Cancelled.</summary>
    Task<PipelineExecutionDto> CancelAsync(string pipelineExecutionId, CancellationToken cancellationToken = default);
}
