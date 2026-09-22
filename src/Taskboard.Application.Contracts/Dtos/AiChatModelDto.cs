namespace Taskboard.Dtos;

public sealed record AiChatModelDto(
    string Id,
    string Provider,
    string Name,
    bool ReasoningEffortSupported,
    /// <summary>Agent CLI that executes this model (SPEC-20260921-ai-chat-cli-backend); null only for legacy/custom entries.</summary>
    string? AgentType = null,
    /// <summary>
    /// Catalog that served this entry (SPEC-20260921-ai-code-thread-config
    /// RF-005): "acp" (reported by the live session), "probe" (CLI-reported),
    /// "curated" (AgentCliModels table) or "custom" (user-registered).
    /// </summary>
    string? Source = null);
