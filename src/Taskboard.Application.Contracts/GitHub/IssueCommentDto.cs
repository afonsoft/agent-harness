namespace Taskboard.GitHub;

/// <summary>
/// One comment on a GitHub issue — the handoff channel between human reviewers
/// and agent runs (SPEC-20260918-github-comments-history).
/// </summary>
public sealed record IssueCommentDto(
    long Id,
    string? AuthorLogin,
    string? Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string HtmlUrl);
