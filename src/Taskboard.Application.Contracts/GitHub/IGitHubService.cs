namespace Taskboard.GitHub;

/// <summary>
/// Serviço de integração com a API do GitHub para sincronização do Taskboard.
/// </summary>
public interface IGitHubService
{
    /// <summary>
    /// Configura o token de autenticação do GitHub (PAT ou token OAuth).
    /// </summary>
    void SetToken(string token);

    /// <summary>
    /// Lista todos os repositórios que o usuário autenticado pode acessar.
    /// </summary>
    Task<IReadOnlyList<RepositoryDto>> GetRepositoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retorna as issues abertas de um repositório e as fechadas dentro da janela
    /// de visibilidade (<see cref="GitHubBoardGrouper.ClosedVisibilityWindow"/>).
    /// </summary>
    Task<IReadOnlyList<IssueDto>> GetIssuesAsync(string repositoryFullName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atualiza a coluna (label) de uma issue, removendo a label anterior e adicionando a nova.
    /// </summary>
    Task<IssueDto> UpdateIssueColumnAsync(string repositoryFullName, int issueNumber, GitHubBoardColumn? oldColumn, GitHubBoardColumn newColumn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cria uma nova issue no repositório e aplica a label inicial correspondente à coluna informada.
    /// </summary>
    Task<IssueDto> CreateIssueAsync(string repositoryFullName, string title, string? body, GitHubBoardColumn initialColumn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adiciona labels extras a uma issue existente.
    /// </summary>
    Task AddLabelsToIssueAsync(string repositoryFullName, int issueNumber, IReadOnlyCollection<string> labels, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atualiza título e/ou corpo (markdown) de uma issue existente.
    /// </summary>
    Task<IssueDto> UpdateIssueAsync(string repositoryFullName, int issueNumber, string? title, string? body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Define a prioridade da issue trocando as labels <c>priority:*</c>
    /// (remove todas as existentes e adiciona a nova; <c>None</c> remove apenas).
    /// </summary>
    Task<IssueDto> SetIssuePriorityAsync(string repositoryFullName, int issueNumber, string priority, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fecha a issue no GitHub. <paramref name="resolution"/>:
    /// <c>canceled</c> aplica a label <c>canceled</c> antes de fechar (coluna Canceled);
    /// <c>archived</c> fecha sem label de coluna (coluna Archived).
    /// </summary>
    Task<IssueDto> CloseIssueAsync(string repositoryFullName, int issueNumber, string resolution, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista os comentários de uma issue em ordem cronológica (mais antigo primeiro).
    /// </summary>
    Task<IReadOnlyList<IssueCommentDto>> GetIssueCommentsAsync(string repositoryFullName, int issueNumber, int take = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adiciona um comentário a uma issue existente.
    /// </summary>
    Task<IssueCommentDto> AddIssueCommentAsync(string repositoryFullName, int issueNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista os milestones de um repositório (abertos e fechados) — o
    /// <c>due_on</c> é o marcador de deadline do Gantt
    /// (SPEC-20260918-gantt-github-timeline).
    /// </summary>
    Task<IReadOnlyList<MilestoneDto>> GetMilestonesAsync(string repositoryFullName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Eventos <c>labeled</c>/<c>unlabeled</c> do timeline de uma issue — usados
    /// para reconstruir as transições de coluna. Best-effort: a implementação
    /// retorna o que conseguir (rate limits não devem derrubar o conjunto).
    /// </summary>
    Task<IReadOnlyList<IssueLabelEventDto>> GetIssueTimelineEventsAsync(string repositoryFullName, int issueNumber, CancellationToken cancellationToken = default);
}
