namespace Taskboard.Requests;

public sealed record CreateAiChatThreadRequest(
    string Title,
    string Model,
    string ReasoningEffort,
    string Sandbox,
    string? Mode = null,
    string? AgentType = null,
    string? WorkspacePath = null,
    string? RepositoryFullName = null,
    /// <summary>SPEC-20260921-ai-code-thread-config RF-004: tier Lite|Normal|Ultra usado quando <see cref="Model"/> está vazio.</summary>
    string? ModelTier = null,
    /// <summary>SPEC-20260928-ai-code-generic-cli RF-003: "acp" (structured) | "pty" (raw terminal). Null → def/ACP default.</summary>
    string? Transport = null,
    /// <summary>SPEC-20260928-ai-code-generic-cli RF-004: "host" (default) | container name — spawns via docker exec.</summary>
    string? ContainerContext = null,
    /// <summary>SPEC-20260928-ai-code-generic-cli RF-002: custom definition id (custom-*) — replaces AgentType.</summary>
    string? AgentCliId = null);
