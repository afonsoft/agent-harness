namespace Taskboard.Harness;

/// <summary>
/// Configurable sandbox policy for a run
/// (SPEC-20260919-harness-security-permission-gateway RF-004).
/// </summary>
public enum SecurityPolicyMode
{
    /// <summary>Approval required for any write or non-allowlisted command.</summary>
    Strict,

    /// <summary>Auto-approves Safe and WorkspaceWrite; approval for Dangerous.</summary>
    Standard,

    /// <summary>Allows everything inside the worktree; blocks sudo/jail escapes.</summary>
    Autonomous
}
