namespace Taskboard.GitHub;

/// <summary>
/// Extensões para converter <see cref="GitHubBoardColumn"/> em labels do GitHub,
/// resolver prioridades a partir de labels <c>priority:*</c> e exibir nomes de coluna.
/// </summary>
public static class GitHubBoardColumnExtensions
{
    private static readonly IReadOnlyDictionary<GitHubBoardColumn, string> LabelMap = new Dictionary<GitHubBoardColumn, string>
    {
        [GitHubBoardColumn.Backlog] = "backlog",
        [GitHubBoardColumn.Todo] = "todo",
        [GitHubBoardColumn.InProgress] = "in-progress",
        [GitHubBoardColumn.InReview] = "in-review",
        [GitHubBoardColumn.InPullRequest] = "in-pullrequest",
        [GitHubBoardColumn.Blocked] = "blocked",
        [GitHubBoardColumn.Done] = "done",
        [GitHubBoardColumn.Canceled] = "canceled"
    };

    private static readonly IReadOnlyDictionary<string, GitHubBoardColumn> LegacyLabelAliases =
        new Dictionary<string, GitHubBoardColumn>(StringComparer.OrdinalIgnoreCase)
        {
            ["review"] = GitHubBoardColumn.InReview
        };

    private static readonly IReadOnlyDictionary<string, string> PriorityMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["priority:urgent"] = "Urgent",
            ["priority:high"] = "High",
            ["priority:medium"] = "Medium",
            ["priority:low"] = "Low"
        };

    /// <summary>
    /// Colunas que possuem label correspondente no GitHub (todas exceto <see cref="GitHubBoardColumn.Archived"/>).
    /// </summary>
    public static IReadOnlyList<GitHubBoardColumn> LabelBackedColumns { get; } =
        Enum.GetValues<GitHubBoardColumn>()
            .Where(c => c != GitHubBoardColumn.Archived)
            .ToList()
            .AsReadOnly();

    /// <summary>
    /// Retorna a label do GitHub correspondente à coluna.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Quando a coluna não possui label (Archived).</exception>
    public static string ToLabel(this GitHubBoardColumn column) =>
        LabelMap.TryGetValue(column, out var label)
            ? label
            : throw new ArgumentOutOfRangeException(nameof(column), column, "Column has no GitHub label.");

    /// <summary>
    /// Retorna o nome de exibição da coluna (mesma convenção de status do board de projetos).
    /// </summary>
    public static string ToDisplayName(this GitHubBoardColumn column) => column switch
    {
        GitHubBoardColumn.Backlog => "backlog",
        GitHubBoardColumn.Todo => "todo",
        GitHubBoardColumn.InProgress => "in_progress",
        GitHubBoardColumn.InReview => "in_review",
        GitHubBoardColumn.InPullRequest => "in_pullrequest",
        GitHubBoardColumn.Blocked => "blocked",
        GitHubBoardColumn.Done => "done",
        GitHubBoardColumn.Canceled => "canceled",
        GitHubBoardColumn.Archived => "archived",
        _ => "archived"
    };

    /// <summary>
    /// Indica se a coluna pode receber uma issue via drag-and-drop ou criação (possui label).
    /// </summary>
    public static bool HasLabel(this GitHubBoardColumn column) => LabelMap.ContainsKey(column);

    /// <summary>
    /// Tenta identificar a coluna do Kanban a partir de uma label do GitHub,
    /// incluindo aliases legados (ex.: <c>review</c> → <see cref="GitHubBoardColumn.InReview"/>).
    /// </summary>
    public static GitHubBoardColumn? FromLabel(string label)
    {
        foreach (var pair in LabelMap)
        {
            if (string.Equals(pair.Value, label, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Key;
            }
        }

        return LegacyLabelAliases.TryGetValue(label, out var column) ? column : null;
    }

    /// <summary>
    /// Retorna todas as labels de coluna suportadas.
    /// </summary>
    public static IReadOnlyCollection<string> GetAllLabels() => LabelMap.Values.ToList().AsReadOnly();

    /// <summary>
    /// Resolve a prioridade de uma issue a partir das labels <c>priority:*</c>.
    /// Retorna <c>"None"</c> quando nenhuma label de prioridade existe.
    /// </summary>
    public static string ResolvePriority(IEnumerable<string> labels)
    {
        foreach (var label in labels)
        {
            if (PriorityMap.TryGetValue(label, out var priority))
            {
                return priority;
            }
        }

        return "None";
    }

    /// <summary>Prioridades selecionáveis no card, em ordem de exibição.</summary>
    public static IReadOnlyList<string> SelectablePriorities { get; } =
        ["None", "Urgent", "High", "Medium", "Low"];

    /// <summary>
    /// Normaliza um valor de prioridade (case-insensitive) para o nome canônico
    /// (<c>Urgent</c>, <c>High</c>, <c>Medium</c>, <c>Low</c>, <c>None</c>) ou
    /// retorna <c>null</c> quando o valor não é suportado.
    /// </summary>
    public static string? NormalizePriority(string? value)
    {
        foreach (var priority in SelectablePriorities)
        {
            if (string.Equals(priority, value?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return priority;
            }
        }

        return null;
    }

    /// <summary>
    /// Retorna a label <c>priority:*</c> correspondente ao nome canônico,
    /// ou <c>null</c> para <c>None</c>/valores inválidos.
    /// </summary>
    public static string? ToPriorityLabel(string priority)
    {
        foreach (var pair in PriorityMap)
        {
            if (string.Equals(pair.Value, priority, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Key;
            }
        }

        return null;
    }

    /// <summary>Indica se a label é uma label de prioridade (<c>priority:*</c>).</summary>
    public static bool IsPriorityLabel(string label) =>
        label.StartsWith("priority:", StringComparison.OrdinalIgnoreCase);
}
