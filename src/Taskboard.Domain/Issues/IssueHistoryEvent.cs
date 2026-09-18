using Taskboard.Issues;

namespace Taskboard.Domain.Issues;

/// <summary>
/// Persisted event from the board's own mutations on a GitHub issue —
/// column moves, edits and closes. Keyed by the GitHub issue id
/// (<see cref="IssueId"/>), the same key used by <c>AgentRun</c>.
/// </summary>
public sealed class IssueHistoryEvent : Entity<Guid>
{
    public string IssueId { get; private set; } = string.Empty;

    public string Repository { get; private set; } = string.Empty;

    public IssueHistoryEventKind Kind { get; private set; }

    public string? From { get; private set; }

    public string? To { get; private set; }

    public string? Detail { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    private IssueHistoryEvent()
    {
    }

    public IssueHistoryEvent(
        Guid id,
        string issueId,
        string repository,
        IssueHistoryEventKind kind,
        DateTimeOffset occurredAt,
        string? from = null,
        string? to = null,
        string? detail = null)
        : base(id)
    {
        IssueId = issueId;
        Repository = repository;
        Kind = kind;
        OccurredAt = occurredAt;
        From = from;
        To = to;
        Detail = detail;
    }
}
