namespace Taskboard.Application.Contracts.Skills;

/// <summary>A skills directory location scanned during verification.</summary>
public sealed record SkillsInstallLocation(string Path, int Count);

/// <summary>
/// Snapshot of the skills installer (SPEC-20260917-skills-installer). Carries
/// the persisted install state plus live prerequisite availability so the
/// Settings UI can render everything from a single payload.
/// </summary>
public sealed record SkillsInstallStatus(
    SkillsSyncState State,
    bool Installed,
    int SkillCount,
    DateTimeOffset? LastRunUtc,
    long? LastDurationMs,
    string? Repository,
    IReadOnlyDictionary<string, bool> Prerequisites,
    IReadOnlyList<SkillsInstallStep> Steps,
    IReadOnlyList<SkillsInstallLocation> Locations,
    string? Error)
{
    public static readonly SkillsInstallStatus Empty = new(
        SkillsSyncState.Idle,
        Installed: false,
        SkillCount: 0,
        LastRunUtc: null,
        LastDurationMs: null,
        Repository: null,
        Prerequisites: new Dictionary<string, bool>(),
        Steps: [],
        Locations: [],
        Error: null);
}
