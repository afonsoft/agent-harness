namespace Taskboard.Agents;

/// <summary>
/// Requisição para execução de um agente CLI sobre uma issue/tarefa.
/// </summary>
public sealed record AgentExecutionRequest(
    string IssueId,
    int IssueNumber,
    string RepositoryFullName,
    string RepoPath,
    string? Branch,
    string? Scope,
    string Instructions,
    AgentType AgentType,
    AgentModelTier ModelTier = AgentModelTier.Normal,
    string? ResolvedModelName = null);
