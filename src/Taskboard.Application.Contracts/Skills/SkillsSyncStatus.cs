namespace Taskboard.Application.Contracts.Skills;

/// <summary>Snapshot of the skills synchronization engine (SPEC-20260915-skills-repo-sync).</summary>
public sealed record SkillsSyncStatus(
    SkillsSyncState State,
    DateTimeOffset? LastRunUtc,
    long? LastDurationMs,
    string? Repository,
    string? Error,
    IReadOnlyList<AgentSyncResult> Agents)
{
    public static readonly SkillsSyncStatus Empty =
        new(SkillsSyncState.Idle, null, null, null, null, []);
}
