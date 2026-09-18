using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Taskboard.Domain.Issues;
using Taskboard.GitHub;
using Taskboard.Issues;
using Taskboard.Repositories;

namespace Taskboard.Application.GitHub;

/// <summary>
/// Builds the Gantt timeline and kanban flow metrics from GitHub issues,
/// milestones and label events plus the locally persisted
/// <see cref="IssueHistoryEvent"/> records
/// (SPEC-20260918-gantt-github-timeline).
/// </summary>
public sealed class TimelineMetricsService(
    IGitHubService gitHub,
    IRepository<IssueHistoryEvent> history,
    ILogger<TimelineMetricsService> logger) : ITimelineMetricsService
{
    internal const int DefaultDays = 90;
    internal const int MaxIssues = 200;
    internal const int ThroughputWeeks = 8;
    private const int MaxConcurrentTimelineFetches = 8;

    private static readonly string BacklogLabel = GitHubBoardColumn.Backlog.ToLabel();
    private static readonly HashSet<string> DoneLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        GitHubBoardColumn.Done.ToLabel(),
        GitHubBoardColumn.Canceled.ToLabel()
    };

    public async Task<RepoTimelineDto> GetTimelineAsync(
        string owner,
        string repo,
        int days,
        CancellationToken cancellationToken = default) =>
        await BuildTimelineAsync($"{owner}/{repo}", days, cancellationToken).ConfigureAwait(false);

    public async Task<RepoMetricsDto> GetMetricsAsync(
        string owner,
        string repo,
        int days,
        CancellationToken cancellationToken = default)
    {
        var timeline = await BuildTimelineAsync($"{owner}/{repo}", days, cancellationToken)
            .ConfigureAwait(false);
        return ComputeMetrics(timeline, days);
    }

    private async Task<RepoTimelineDto> BuildTimelineAsync(
        string fullName,
        int days,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var windowStart = now - TimeSpan.FromDays(days);

        var issuesTask = gitHub.GetIssuesAsync(fullName, cancellationToken);
        var milestonesTask = gitHub.GetMilestonesAsync(fullName, cancellationToken);
        await Task.WhenAll(issuesTask, milestonesTask).ConfigureAwait(false);

        // An issue overlaps the window when its bar (createdAt → closedAt/today)
        // touches it: open issues always do; closed issues must have closed
        // inside the window.
        var issues = issuesTask.Result
            .Where(i => i.ClosedAt is null || i.ClosedAt >= windowStart)
            .OrderBy(i => i.CreatedAt)
            .Take(MaxIssues)
            .ToList();

        var localEvents = await history.Query
            .Where(e => e.Repository == fullName && e.Kind == IssueHistoryEventKind.ColumnMoved)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var localByIssue = localEvents
            .GroupBy(e => e.IssueId)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // RF-007: label events are per-issue GitHub calls — bounded parallelism,
        // best-effort per issue (a failure leaves the issue without
        // transitions rather than failing the whole timeline).
        using var gate = new SemaphoreSlim(MaxConcurrentTimelineFetches);
        var fetchTasks = issues.Select(async issue =>
        {
            try
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    return await gitHub
                        .GetIssueTimelineEventsAsync(fullName, issue.Number, cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Timeline events fetch failed for issue #{Number} in {Repository}.",
                    issue.Number,
                    fullName);
                return (IReadOnlyList<IssueLabelEventDto>)[];
            }
        });

        var labelEvents = await Task.WhenAll(fetchTasks).ConfigureAwait(false);

        var timeline = new List<IssueTimelineDto>(issues.Count);
        for (var i = 0; i < issues.Count; i++)
        {
            var issue = issues[i];
            localByIssue.TryGetValue(
                issue.Id.ToString(CultureInfo.InvariantCulture),
                out var local);

            timeline.Add(new IssueTimelineDto(
                issue.Id,
                issue.Number,
                issue.Title,
                ColumnLabel(issue.Column),
                issue.Priority,
                issue.AssigneeLogin,
                issue.CreatedAt,
                issue.ClosedAt,
                issue.MilestoneNumber,
                issue.MilestoneDueOn,
                BuildTransitions(labelEvents[i], local)));
        }

        return new RepoTimelineDto(timeline, milestonesTask.Result);
    }

    /// <summary>
    /// Replays label events chronologically under the board's single-column
    /// invariant (at most one column label at a time), merges the locally
    /// persisted moves and de-duplicates by (at, from, to).
    /// </summary>
    internal static IReadOnlyList<IssueTransitionDto> BuildTransitions(
        IReadOnlyList<IssueLabelEventDto> labelEvents,
        IReadOnlyList<IssueHistoryEvent>? localEvents)
    {
        var transitions = new List<IssueTransitionDto>();
        string? current = null;
        foreach (var e in labelEvents.OrderBy(e => e.At))
        {
            if (GitHubBoardColumnExtensions.FromLabel(e.Label) is null)
            {
                continue;
            }

            if (e.Added)
            {
                if (current == e.Label)
                {
                    continue;
                }

                transitions.Add(new IssueTransitionDto(e.At, current, e.Label));
                current = e.Label;
            }
            else if (current is not null && current.Equals(e.Label, StringComparison.OrdinalIgnoreCase))
            {
                transitions.Add(new IssueTransitionDto(e.At, e.Label, null));
                current = null;
            }
        }

        if (localEvents is not null)
        {
            transitions.AddRange(localEvents.Select(
                e => new IssueTransitionDto(e.OccurredAt, e.From, e.To)));
        }

        return transitions
            .DistinctBy(t => (t.At, t.From, t.To))
            .OrderBy(t => t.At)
            .ToList();
    }

    internal static RepoMetricsDto ComputeMetrics(RepoTimelineDto timeline, int days)
    {
        var now = DateTimeOffset.UtcNow;
        var windowStart = now - TimeSpan.FromDays(days);

        var closedInWindow = timeline.Issues
            .Where(i => i.ClosedAt is not null && i.ClosedAt >= windowStart)
            .ToList();

        var leadTimes = closedInWindow
            .Select(i => (i.ClosedAt!.Value - i.CreatedAt).TotalDays)
            .Where(d => d >= 0)
            .ToList();

        var cycleTimes = closedInWindow
            .Select(CycleTimeDays)
            .OfType<double>()
            .ToList();

        var wip = timeline.Issues.Count(i =>
            i.ClosedAt is null
            && !string.Equals(i.Column, BacklogLabel, StringComparison.OrdinalIgnoreCase));

        var openAges = timeline.Issues
            .Where(i => i.ClosedAt is null)
            .Select(i => (now - i.CreatedAt).TotalDays)
            .Where(d => d >= 0)
            .ToList();

        return new RepoMetricsDto(
            leadTimes.Count > 0 ? leadTimes.Average() : null,
            Median(leadTimes),
            cycleTimes.Count > 0 ? cycleTimes.Average() : null,
            Throughput(closedInWindow, now),
            wip,
            Median(openAges));
    }

    /// <summary>
    /// Cycle time = first exit from backlog → done/canceled/closed. The exit is
    /// the first transition into a non-backlog column label; the finish is the
    /// first transition into done/canceled, or the close date.
    /// </summary>
    private static double? CycleTimeDays(IssueTimelineDto issue)
    {
        var startedAt = issue.Transitions
            .Where(t => t.To is not null
                && !string.Equals(t.To, BacklogLabel, StringComparison.OrdinalIgnoreCase))
            .Select(t => (DateTimeOffset?)t.At)
            .FirstOrDefault();
        if (startedAt is null)
        {
            return null;
        }

        var doneAt = issue.Transitions
            .Where(t => t.To is not null && DoneLabels.Contains(t.To))
            .Select(t => (DateTimeOffset?)t.At)
            .FirstOrDefault() ?? issue.ClosedAt;
        if (doneAt is null || doneAt < startedAt)
        {
            return null;
        }

        return (doneAt.Value - startedAt.Value).TotalDays;
    }

    private static IReadOnlyList<ThroughputPointDto> Throughput(
        List<IssueTimelineDto> closedInWindow,
        DateTimeOffset now)
    {
        var weekStart = WeekStart(now);
        var points = Enumerable.Range(0, ThroughputWeeks)
            .Select(w => new ThroughputPointDto(DateOnly.FromDateTime(weekStart.AddDays(-7 * w).UtcDateTime), 0))
            .Reverse()
            .ToList();

        foreach (var issue in closedInWindow)
        {
            var issueWeek = WeekStart(issue.ClosedAt!.Value);
            var index = points.FindIndex(
                p => p.WeekStart == DateOnly.FromDateTime(issueWeek.UtcDateTime));
            if (index >= 0)
            {
                points[index] = points[index] with { Closed = points[index].Closed + 1 };
            }
        }

        return points;
    }

    private static DateTimeOffset WeekStart(DateTimeOffset value)
    {
        var date = value.UtcDateTime.Date;
        var offset = ((int)date.DayOfWeek + 6) % 7; // Monday = 0
        return new DateTimeOffset(date.AddDays(-offset), TimeSpan.Zero);
    }

    internal static double? Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>
    /// Column label for the timeline DTO — <see cref="GitHubBoardColumn.Archived"/>
    /// has no label, so it falls back to its display name.
    /// </summary>
    private static string ColumnLabel(GitHubBoardColumn column) =>
        column == GitHubBoardColumn.Archived ? "archived" : column.ToLabel();
}
