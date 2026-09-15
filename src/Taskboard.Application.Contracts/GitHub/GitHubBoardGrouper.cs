namespace Taskboard.GitHub;

/// <summary>
/// Lógica pura de agrupamento, visibilidade e filtro de issues do GitHub no board.
/// </summary>
public static class GitHubBoardGrouper
{
    /// <summary>
    /// Janela de tempo em que issues fechadas continuam visíveis no board.
    /// </summary>
    public static readonly TimeSpan ClosedVisibilityWindow = TimeSpan.FromDays(90);

    // Precedência para issues abertas com múltiplas labels de status.
    private static readonly GitHubBoardColumn[] OpenPrecedence =
    [
        GitHubBoardColumn.Done,
        GitHubBoardColumn.Canceled,
        GitHubBoardColumn.InPullRequest,
        GitHubBoardColumn.InReview,
        GitHubBoardColumn.InProgress,
        GitHubBoardColumn.Blocked,
        GitHubBoardColumn.Todo,
        GitHubBoardColumn.Backlog
    ];

    /// <summary>
    /// Indica se a issue deve aparecer no board: abertas sempre; fechadas apenas
    /// dentro da janela de <see cref="ClosedVisibilityWindow"/>.
    /// </summary>
    public static bool IsVisible(IssueDto issue, DateTimeOffset now)
    {
        if (!IsClosed(issue))
        {
            return true;
        }

        return issue.ClosedAt is null || issue.ClosedAt.Value >= now - ClosedVisibilityWindow;
    }

    /// <summary>
    /// Resolve a coluna da issue.
    /// Aberta: label de status de maior precedência; sem label → Backlog.
    /// Fechada: label done → Done; canceled → Canceled; demais → Archived.
    /// </summary>
    public static GitHubBoardColumn ResolveColumn(IssueDto issue)
    {
        if (IsClosed(issue))
        {
            if (HasLabel(issue, GitHubBoardColumn.Done))
            {
                return GitHubBoardColumn.Done;
            }

            if (HasLabel(issue, GitHubBoardColumn.Canceled))
            {
                return GitHubBoardColumn.Canceled;
            }

            return GitHubBoardColumn.Archived;
        }

        foreach (var column in OpenPrecedence)
        {
            if (HasLabel(issue, column))
            {
                return column;
            }
        }

        return GitHubBoardColumn.Backlog;
    }

    /// <summary>
    /// Agrupa as issues visíveis por coluna, na ordem do enum, ordenadas por UpdatedAt desc.
    /// </summary>
    public static IReadOnlyList<BoardColumnDto> GroupByColumn(IEnumerable<IssueDto> issues, DateTimeOffset now)
    {
        var visible = issues.Where(i => IsVisible(i, now)).ToList();

        return Enum.GetValues<GitHubBoardColumn>()
            .Select(column => new BoardColumnDto(
                column,
                column.ToDisplayName(),
                visible.Where(i => i.Column == column)
                    .OrderByDescending(i => i.UpdatedAt ?? i.CreatedAt)
                    .ToList()
                    .AsReadOnly()))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Filtro de texto e prioridade no mesmo padrão do board de projetos.
    /// </summary>
    public static bool MatchesFilters(IssueDto issue, string? filterText, string? filterPriority)
    {
        if (!string.IsNullOrWhiteSpace(filterPriority)
            && !string.Equals(issue.Priority, filterPriority, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(filterText))
        {
            return true;
        }

        var text = filterText.Trim();
        return issue.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
            || (issue.Body?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
            || issue.Labels.Any(l => l.Contains(text, StringComparison.OrdinalIgnoreCase))
            || issue.Number.ToString().Contains(text, StringComparison.Ordinal);
    }

    private static bool IsClosed(IssueDto issue) =>
        string.Equals(issue.State, "closed", StringComparison.OrdinalIgnoreCase);

    private static bool HasLabel(IssueDto issue, GitHubBoardColumn column) =>
        issue.Labels.Any(l => GitHubBoardColumnExtensions.FromLabel(l) == column);
}
