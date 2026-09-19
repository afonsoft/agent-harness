namespace Taskboard.Dtos;

public sealed record AiChatEventDto(
    string Id,
    string ThreadId,
    string Role,
    string Content,
    DateTime CreatedAt,
    string Kind = "message",
    string? PayloadJson = null);
