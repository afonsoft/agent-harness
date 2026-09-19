namespace Taskboard.Requests;

public sealed record CreateAiChatThreadRequest(
    string Title,
    string Model,
    string ReasoningEffort,
    string Sandbox,
    string? Mode = null,
    string? AgentType = null,
    string? WorkspacePath = null,
    string? RepositoryFullName = null);
