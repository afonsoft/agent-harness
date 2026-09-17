using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// Snapshot de uma execução de agente sobre uma issue (SPEC-20260917-agent-eligibility-task-badge).
/// </summary>
public sealed record AgentRunDto(
    Guid Id,
    string IssueId,
    AgentType AgentType,
    AgentRunState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);
