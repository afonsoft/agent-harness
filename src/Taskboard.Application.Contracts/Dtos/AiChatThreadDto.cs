namespace Taskboard.Dtos;

public sealed record AiChatThreadDto(
    string Id,
    string Title,
    string Model,
    string ReasoningEffort,
    string Sandbox,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    long Version,
    string Mode = "assistant",
    string? AgentType = null,
    string? WorkspacePath = null,
    string? RepositoryFullName = null,
    /// <summary>SPEC-20260921-ai-code-thread-config RF-006: tier usado na escolha do modelo.</summary>
    string? ModelTier = null,
    /// <summary>SPEC-20260921-ai-code-thread-config RF-006: origem do modelo efetivo (acp|probe|curated|custom).</summary>
    string? ModelSource = null)
{
    /// <summary>
    /// SPEC-20260928-ai-code-ux-simplify RF-005: thread kind — <c>"chat"</c>
    /// (structured ACP events) or <c>"terminal"</c> (raw PTY session; populated
    /// by SPEC-20260928-ai-code-generic-cli). Defaults to <c>"chat"</c>.
    /// </summary>
    public string Kind { get; init; } = "chat";

    /// <summary>SPEC-20260928 RF-003: "acp" | "pty" — immutable after creation.</summary>
    public string Transport { get; init; } = "acp";

    /// <summary>SPEC-20260928 RF-004: container name for docker exec; null → host.</summary>
    public string? ContainerContext { get; init; }

    /// <summary>SPEC-20260928 RF-002: custom CLI definition id (custom-*); null → builtin AgentType.</summary>
    public string? AgentCliId { get; init; }
}
