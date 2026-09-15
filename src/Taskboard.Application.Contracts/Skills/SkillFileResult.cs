namespace Taskboard.Application.Contracts.Skills;

/// <summary>Outcome of reading a file inside a skill directory.</summary>
public enum SkillFileError
{
    /// <summary>File content was returned.</summary>
    None,

    /// <summary>Skill or file does not exist (also used for hidden paths).</summary>
    NotFound,

    /// <summary>Path is rooted, empty, or contains invalid segments such as <c>..</c>.</summary>
    InvalidPath,

    /// <summary>File exceeds the maximum allowed size.</summary>
    TooLarge,

    /// <summary>File extension is not in the text whitelist or is sensitive.</summary>
    NotText
}

/// <summary>Result payload for <c>GetFileAsync</c>.</summary>
public sealed record SkillFileResult(SkillFileError Error, string? RelativePath, string? Content)
{
    public static SkillFileResult Ok(string relativePath, string content) =>
        new(SkillFileError.None, relativePath, content);

    public static SkillFileResult Fail(SkillFileError error) => new(error, null, null);
}
