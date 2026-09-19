namespace Taskboard.Dtos;

/// <summary>Persisted cross-session memory item (DTO-facing, never an entity).</summary>
public sealed record ProjectMemoryItemDto(
    string Id,
    string RepositoryFullName,
    string Topic,
    string Content,
    string Type,
    IReadOnlyList<string> Tags,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    long Version);

/// <summary>Payload for <c>POST /api/harness/memory</c> (SPEC §5).</summary>
public sealed record AddMemoryRequestDto(
    string RepositoryFullName,
    string Topic,
    string Content,
    IReadOnlyList<string>? Tags = null,
    string? Type = null);
