namespace Taskboard.Issues;

/// <summary>
/// Kind of a persisted GitHub-issue history event (column moves, edits,
/// closes recorded by the board's own API). Agent executions are persisted
/// separately as <c>AgentRun</c> and merged into the same timeline.
/// </summary>
public enum IssueHistoryEventKind
{
    ColumnMoved,
    Edited,
    Closed,
    /// <summary>Pipeline run started for the issue (SPEC-20260920-board-cockpit-unified-runs R7).</summary>
    RunStarted
}
