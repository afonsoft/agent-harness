namespace Taskboard.Harness;

/// <summary>
/// Risk classification of a command/tool call
/// (SPEC-20260919-harness-security-permission-gateway RF-001).
/// </summary>
public enum SecurityRiskLevel
{
    /// <summary>Read-only operations: file reads, git status, builds, tests.</summary>
    Safe,

    /// <summary>Creates or edits files inside the designated worktree.</summary>
    WorkspaceWrite,

    /// <summary>Recursive deletes, sudo, network egress, jail escapes.</summary>
    Dangerous
}
