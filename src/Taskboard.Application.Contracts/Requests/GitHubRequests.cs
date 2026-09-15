using Taskboard.GitHub;

namespace Taskboard.Requests;

public sealed record CreateGitHubIssueRequest(string Title, string? Body, GitHubBoardColumn InitialColumn);

public sealed record UpdateGitHubIssueColumnRequest(GitHubBoardColumn? OldColumn, GitHubBoardColumn NewColumn);

public sealed record AddGitHubLabelsRequest(IReadOnlyCollection<string> Labels);
