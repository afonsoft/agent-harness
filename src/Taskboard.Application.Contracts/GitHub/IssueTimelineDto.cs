namespace Taskboard.GitHub;

/// <summary>
/// One issue on the Gantt timeline: bar = <see cref="CreatedAt"/> →
/// <see cref="ClosedAt"/> (open issues extend to today), plus the column
/// transitions replayed from label events and local history.
/// </summary>
public sealed record IssueTimelineDto(
    long Id,
    int Number,
    string Title,
    string Column,
    string Priority,
    string? AssigneeLogin,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    int? MilestoneNumber,
    DateTimeOffset? MilestoneDueOn,
    IReadOnlyList<IssueTransitionDto> Transitions);

/// <summary>Response of <c>GET /api/github/repos/{owner}/{repo}/timeline</c>.</summary>
public sealed record RepoTimelineDto(
    IReadOnlyList<IssueTimelineDto> Issues,
    IReadOnlyList<MilestoneDto> Milestones);
