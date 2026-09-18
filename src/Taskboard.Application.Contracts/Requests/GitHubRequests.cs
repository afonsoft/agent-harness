using Taskboard.GitHub;

namespace Taskboard.Requests;

public sealed record CreateGitHubIssueRequest(string Title, string? Body, GitHubBoardColumn InitialColumn);

public sealed record UpdateGitHubIssueColumnRequest(GitHubBoardColumn? OldColumn, GitHubBoardColumn NewColumn);

public sealed record AddGitHubLabelsRequest(IReadOnlyCollection<string> Labels);

public sealed record UpdateGitHubIssueRequest(string? Title, string? Body);

public sealed record SetIssuePriorityRequest(string Priority);

public sealed record CloseGitHubIssueRequest(string Resolution);
