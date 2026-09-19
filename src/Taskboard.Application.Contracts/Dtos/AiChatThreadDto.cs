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
    long Version);
