namespace Taskboard.Application.Contracts.Skills;

public sealed record SkillDetailDto(
    string Name,
    string Description,
    string Source,
    string Path,
    IReadOnlyList<string> Tools,
    string? References,
    string? Scripts,
    string Content,
    IReadOnlyList<SkillFileDto> Files);
