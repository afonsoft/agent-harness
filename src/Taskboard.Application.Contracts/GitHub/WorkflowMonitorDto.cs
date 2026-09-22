namespace Taskboard.GitHub;

/// <summary>
/// SPEC-20260922-workflow-actions-resilience — /workflow monitor payload:
/// workflows with last-run badges, the repo's latest runs (cross-workflow)
/// and a flag marking partial data when the runs enrichment degraded.
/// </summary>
public sealed record WorkflowMonitorDto(
    IReadOnlyList<WorkflowDto> Workflows,
    IReadOnlyList<WorkflowRunDto> RecentRuns,
    bool Degraded);
