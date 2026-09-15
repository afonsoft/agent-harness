namespace Taskboard.Application.Contracts.Skills;

/// <summary>A file inside a skill directory.</summary>
/// <param name="RelativePath">Path relative to the skill root, always <c>/</c>-separated.</param>
/// <param name="SizeBytes">File size in bytes.</param>
/// <param name="IsText">Whether the file can be served as text by the file-content endpoint.</param>
public sealed record SkillFileDto(string RelativePath, long SizeBytes, bool IsText);
