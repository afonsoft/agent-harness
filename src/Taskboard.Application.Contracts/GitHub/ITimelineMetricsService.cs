namespace Taskboard.GitHub;

/// <summary>
/// Builds the Gantt timeline (issues + milestones + column transitions) and
/// the kanban flow metrics for a repository
/// (SPEC-20260918-gantt-github-timeline).
/// </summary>
public interface ITimelineMetricsService
{
    /// <summary>
    /// Issues overlapping the last <paramref name="days"/> days (bars
    /// createdAt → closedAt/today), milestones, and per-issue column
    /// transitions replayed from GitHub label events + local history.
    /// </summary>
    Task<RepoTimelineDto> GetTimelineAsync(
        string owner,
        string repo,
        int days,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lead/cycle time, weekly throughput, WIP and aging over the same
    /// <paramref name="days"/> window.
    /// </summary>
    Task<RepoMetricsDto> GetMetricsAsync(
        string owner,
        string repo,
        int days,
        CancellationToken cancellationToken = default);
}
