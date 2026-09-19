namespace Taskboard.Requests;

public sealed record CreateAiChatThreadRequest(
    string Title,
    string Model,
    string ReasoningEffort,
    string Sandbox);
