namespace Taskboard.Application.Contracts.Skills;

/// <summary>
/// One row of <c>npx skills find &lt;q&gt;</c> output
/// (SPEC-20261010-mcp-skills-hub RF-005). <see cref="Repository"/> is the bare
/// <c>owner/repo</c>; when the row named one skill it lands in
/// <see cref="Skill"/> — the pair feeds <c>POST /api/skills/install-one</c>
/// directly.
/// </summary>
public sealed record SkillSearchResultDto(
    string Name,
    string Repository,
    string? Skill,
    string? Description);

/// <summary>Body for POST /api/skills/install-repo — optional repo override.</summary>
public sealed record SkillRepoInstallRequest(string? Repository);

/// <summary>Body for POST /api/skills/install-one — single skill from a repo.</summary>
public sealed record SkillInstallRequest(string Repository, string? Skill);
