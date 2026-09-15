namespace Taskboard.Application.Contracts.Skills;

/// <summary>Lifecycle state of the skills synchronization engine.</summary>
public enum SkillsSyncState
{
    Idle,
    Running,
    Succeeded,
    Failed
}
