namespace Taskboard.GitHub;

/// <summary>
/// Colunas padrão do quadro Kanban mapeadas para labels do GitHub.
/// <see cref="Archived"/> é uma pseudo-coluna derivada (issues fechadas sem label
/// done/canceled) e não possui label correspondente.
/// </summary>
public enum GitHubBoardColumn
{
    Backlog,
    Todo,
    InProgress,
    InReview,
    InPullRequest,
    Blocked,
    Done,
    Canceled,
    Archived
}
