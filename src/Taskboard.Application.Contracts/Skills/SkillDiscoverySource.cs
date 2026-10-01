namespace Taskboard.Application.Contracts.Skills;

public sealed record SkillDiscoverySource(string Source, string Path)
{
    /// <summary>Canonical shared skills dir (<c>~/.agents/skills</c>) — the only
    /// source surfaced in chat capabilities, the slash palette and the LLM
    /// skill catalog. Other agent dirs remain visible to the Skills page.</summary>
    public const string Agents = "agents";
}
