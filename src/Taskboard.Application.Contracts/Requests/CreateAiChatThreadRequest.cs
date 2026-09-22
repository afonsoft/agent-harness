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
    string? ModelTier = null);
