using Taskboard.Agents;

namespace Taskboard.GitHub;

/// <summary>
/// One entry in the unified issue timeline — either a persisted board event
/// (column move, edit, close) or an agent run, newest first.
/// </summary>
public sealed record IssueHistoryItemDto(
    string Kind,
    DateTimeOffset OccurredAt,
    AgentType? AgentType = null,
    AgentRunState? AgentRunState = null,
    DateTimeOffset? FinishedAt = null,
    string? From = null,
    string? To = null,
    string? Detail = null)
{
    public const string AgentRun = "agent-run";
    public const string ColumnMoved = "column-moved";
    public const string Edited = "edited";
    public const string Closed = "closed";
    /// <summary>Pipeline execution started — `Detail` carries the run id (`/cockpit/runs/{id}`).</summary>
    public const string PipelineRun = "pipeline-run";
}
