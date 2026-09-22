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

    /// <summary>Recent executions, newest first — backs `GET /api/harness/runs`.</summary>
    Task<IReadOnlyList<PipelineExecutionDto>> ListAsync(int take = 50, CancellationToken cancellationToken = default);

    /// <summary>Creates the execution and dispatches eligible stages.</summary>
    Task<PipelineExecutionDto> StartAsync(PipelineStartRequest request, CancellationToken cancellationToken = default);

    /// <summary>Snapshot of a running/finished pipeline.</summary>
    Task<PipelineExecutionDto?> GetAsync(string pipelineExecutionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Latest execution launched for a board issue (any status, newest first) —
    /// routes <c>issue:</c>-scoped control/state lookups to the pipeline run the
    /// board started (SPEC-20260921-board-cockpit-agent-observability RF-004).
    /// </summary>
    Task<PipelineExecutionDto?> GetLatestByIssueAsync(string issueId, CancellationToken cancellationToken = default);

    /// <summary>Approves a `WaitingApproval` stage; dependents become eligible (RF gates).</summary>
    Task<PipelineExecutionDto> ApproveStageAsync(
        string pipelineExecutionId, string stageKey, string? comment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects a `WaitingApproval` stage — fails it with the reviewer's reason
    /// (SPEC-20260919-ade-cockpit-hitl RF-004 deny path).
    /// </summary>
    Task<PipelineExecutionDto> RejectStageAsync(
        string pipelineExecutionId, string stageKey, string? comment, CancellationToken cancellationToken = default);

    /// <summary>Re-queues a failed stage with an optional corrected prompt (RF-005).</summary>
    Task<PipelineExecutionDto> RetryStageAsync(
        string pipelineExecutionId, string stageKey, string? adjustedPrompt, CancellationToken cancellationToken = default);

    /// <summary>Safe shutdown of running stages; pipeline → Cancelled.</summary>
    Task<PipelineExecutionDto> CancelAsync(string pipelineExecutionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses the execution between stages — in-flight stages finish, nothing
    /// new is dispatched (SPEC-20260920-cockpit-pause-resume). `null` when the
    /// run does not exist; invalid transitions throw `InvalidPipelineState` (409).
    /// </summary>
    Task<PipelineExecutionDto?> PauseAsync(string pipelineExecutionId, CancellationToken cancellationToken = default);

    /// <summary>Resumes a paused execution and re-dispatches pending stages.</summary>
    Task<PipelineExecutionDto?> ResumeAsync(string pipelineExecutionId, CancellationToken cancellationToken = default);
}
