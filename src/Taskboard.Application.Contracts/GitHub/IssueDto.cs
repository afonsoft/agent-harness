namespace Taskboard.GitHub;

/// <summary>
/// DTO que representa uma issue do GitHub no contexto do quadro Kanban.
/// </summary>
public sealed record IssueDto(
    long Id,
    int Number,
    string Title,
    string? Body,
    string State,
    string Url,
    string HtmlUrl,
    IReadOnlyList<string> Labels,
    GitHubBoardColumn Column,
    string? AssigneeLogin,
    string Priority,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? ClosedAt,
    int? MilestoneNumber = null,
    DateTimeOffset? MilestoneDueOn = null);
