namespace Taskboard.GitHub;

/// <summary>
/// Run de um GitHub Actions workflow (monitor read-only —
/// SPEC-20260918-workflow-github-actions).
/// </summary>
public sealed record WorkflowRunDto(
    long Id,
    string? Name,
    string? DisplayTitle,
    long RunNumber,
    string? Event,
    string Status,
    string? Conclusion,
    string? HeadBranch,
    string? HeadSha,
    string? ActorLogin,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset RunStartedAt,
    string HtmlUrl,
    /// <summary>Workflow that owns the run — groups repo-level run lists (SPEC-20260922-workflow-actions-resilience).</summary>
    long WorkflowId = 0);
