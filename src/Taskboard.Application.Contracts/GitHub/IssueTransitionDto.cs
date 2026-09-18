namespace Taskboard.GitHub;

/// <summary>
/// One column transition reconstructed from GitHub label events and/or
/// persisted <c>IssueHistoryEvent</c> records. <see cref="From"/>/
/// <see cref="To"/> hold column label names (e.g. <c>in-progress</c>);
/// <c>null</c> means "no column label" (backlog).
/// </summary>
public sealed record IssueTransitionDto(
    DateTimeOffset At,
    string? From,
    string? To);
