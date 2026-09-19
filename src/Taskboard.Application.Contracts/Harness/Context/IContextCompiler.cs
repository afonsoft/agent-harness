using Taskboard.Agents;
using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.Harness;

/// <summary>
/// Assembles the dynamic system prompt for an agent run — instruction files
/// (AGENTS.md/CLAUDE.md/…), environment metadata and git context
/// (SPEC-20260919-harness-context-memory RF-001/RF-002).
/// </summary>
public interface IContextCompiler
{
    Task<ContextCompilationDto> CompileAsync(
        string worktreePath,
        AgentType agentType,
        int maxTokenBudget,
        CancellationToken cancellationToken = default);
}
