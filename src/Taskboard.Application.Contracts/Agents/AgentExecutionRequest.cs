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
    string? ResolvedModelName = null,
    /// <summary>Opt-in: run the verification loop on this solution after the agent finishes (SPEC-20260919-harness-verification-loop).</summary>
    string? VerifySolutionFile = null,
    double? VerifyMinCoverage = null,
    int? VerifyMaxAttempts = null);
