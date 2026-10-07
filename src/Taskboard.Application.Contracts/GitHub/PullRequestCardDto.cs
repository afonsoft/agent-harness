namespace Taskboard.GitHub;

/// <summary>
/// SPEC-20261014-chat-git-bar-overview RF-004: resumo de um pull request para
/// o hover card do chat (título, estado, rollup de checks, autor).
/// </summary>
public sealed record PullRequestCardDto(
    int Number,
    string Url,
    string Title,
    /// <summary><c>open</c>/<c>closed</c>.</summary>
    string State,
    bool Merged,
    string? Author,
    int ChecksTotal,
    int ChecksSucceeded,
    int ChecksFailed);
