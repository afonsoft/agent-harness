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
    /// <summary>When true, no model flag is emitted at all — the CLI picks its own default (SPEC-20260921-ai-chat-cli-backend).</summary>
    bool OmitModelFlag = false,
    /// <summary>Opt-in: run the verification loop on this solution after the agent finishes (SPEC-20260919-harness-verification-loop).</summary>
    string? VerifySolutionFile = null,
    double? VerifyMinCoverage = null,
    int? VerifyMaxAttempts = null,
    /// <summary>Budget cap in USD — the run is interrupted with state BudgetExceeded when exceeded (SPEC-20260919-ade-observability-finops RF-003).</summary>
    decimal? MaxBudgetUsd = null,
    /// <summary>Running Docker container to exec the CLI into (SPEC-20260929-docker-cli-context); null = host.</summary>
    string? ContainerContext = null,
    /// <summary>
    /// Existing workspace-isolation session id when <see cref="RepoPath"/> already
    /// points at the session's worktree (delegated tasks). The orchestrator reuses
    /// that session instead of worktree-ing it again — otherwise agent writes
    /// strand in a second run worktree and compare/promote diff an empty tree.
    /// </summary>
    string? ExistingWorktreeRunId = null);
