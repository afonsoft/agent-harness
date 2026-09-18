namespace Taskboard.GitHub;

/// <summary>Issues closed in one calendar week (Monday start, UTC).</summary>
public sealed record ThroughputPointDto(DateOnly WeekStart, int Closed);

/// <summary>
/// Kanban flow metrics computed server-side over the same timeline as the
/// Gantt (SPEC-20260918-gantt-github-timeline).
/// </summary>
public sealed record RepoMetricsDto(
    double? LeadTimeAvgDays,
    double? LeadTimeMedianDays,
    double? CycleTimeAvgDays,
    IReadOnlyList<ThroughputPointDto> ThroughputPerWeek,
    int Wip,
    double? OpenMedianAgeDays);
