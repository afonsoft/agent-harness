namespace Taskboard.Application.Contracts.Skills;

/// <summary>Lifecycle state of a single install step (SPEC-20260917-skills-installer).</summary>
public enum SkillsInstallStepState
{
    Pending,
    Running,
    Succeeded,
    Skipped,
    Failed
}

/// <summary>Outcome of one install step (e.g. <c>npx-add</c>, <c>install-sh</c>).</summary>
public sealed record SkillsInstallStep(
    string Name,
    SkillsInstallStepState State,
    int? ExitCode,
    long? DurationMs,
    string? Message);
