using Taskboard.Agents;

namespace Taskboard.Dtos;

/// <summary>Payload for <c>POST /api/harness/context/compile</c> (SPEC §5).</summary>
public sealed record CompileContextRequestDto(
    string WorktreePath,
    AgentType AgentType,
    int MaxTokenBudget);
