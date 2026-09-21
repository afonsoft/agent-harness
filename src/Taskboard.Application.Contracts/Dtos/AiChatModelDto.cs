namespace Taskboard.Dtos;

public sealed record AiChatModelDto(
    string Id,
    string Provider,
    string Name,
    bool ReasoningEffortSupported,
    /// <summary>Agent CLI that executes this model (SPEC-20260921-ai-chat-cli-backend); null only for legacy/custom entries.</summary>
    string? AgentType = null);
