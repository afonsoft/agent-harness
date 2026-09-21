using Taskboard.GitHub;

namespace Taskboard.Requests;

public sealed record CreateGitHubIssueRequest(string Title, string? Body, GitHubBoardColumn InitialColumn);

public sealed record UpdateGitHubIssueColumnRequest(GitHubBoardColumn? OldColumn, GitHubBoardColumn NewColumn);

public sealed record AddGitHubLabelsRequest(IReadOnlyCollection<string> Labels);

public sealed record UpdateGitHubIssueRequest(string? Title, string? Body);

public sealed record SetIssuePriorityRequest(string Priority);

public sealed record CloseGitHubIssueRequest(string Resolution);

public sealed record AddIssueCommentRequest(string? Body);

/// <summary>Body for `POST /api/github/repos/{owner}/{repo}/pulls` (SPEC-20260919-ade-cockpit-hitl RF-005).</summary>
public sealed record CreatePullRequestBody(string Title, string Head, string BaseBranch, string? Body);
