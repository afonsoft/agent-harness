using System.Text.RegularExpressions;

namespace Taskboard.Application.Contracts.Skills;

/// <summary>
/// Shared input contract for skills installs (SPEC-20261010-mcp-skills-hub):
/// repository shapes accepted by <c>npx skills add</c> and the skill-name
/// pattern for granular installs. Used by the endpoints to validate and by
/// the installer service — one source of truth.
/// </summary>
public static class SkillInputValidation
{
    /// <summary><c>owner/repo</c> (owner accepts dots/underscores/hyphens).</summary>
    private static readonly Regex OwnerRepoPattern = new(
        @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$",
        RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>Skill name: <c>[a-z0-9-_]{1,64}</c>.</summary>
    private static readonly Regex SkillNamePattern = new(
        @"^[a-z0-9-_]{1,64}$",
        RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>
    /// A skills repository is valid when it is <c>owner/repo</c>, an absolute
    /// URL (https/ssh/file/…) or a rooted filesystem path — the same shapes
    /// the installer normalizes.
    /// </summary>
    public static bool IsValidRepository(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return OwnerRepoPattern.IsMatch(trimmed)
            || Uri.TryCreate(trimmed, UriKind.Absolute, out _)
            || Path.IsPathRooted(trimmed);
    }

    /// <summary>Single-skill name check for granular installs.</summary>
    public static bool IsValidSkillName(string? skill)
        => !string.IsNullOrWhiteSpace(skill) && SkillNamePattern.IsMatch(skill.Trim());
}
