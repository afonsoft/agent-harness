using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.Harness;

/// <summary>
/// Pre-fork classification of shell commands into risk levels
/// (SPEC-20260919-harness-security-permission-gateway RF-001).
/// Fail-closed: unparseable or unknown commands classify as Dangerous.
/// </summary>
public interface ICommandRiskClassifier
{
    /// <summary>
    /// Classifies a shell command line. Path arguments are resolved against
    /// <paramref name="worktreePath"/> — targets outside the jail classify as
    /// Dangerous (SPEC RF-001/RF-002; §5 `rm -rf` inside the worktree is
    /// WorkspaceWrite, `rm -rf /` is Dangerous).
    /// </summary>
    CommandRiskAssessment Classify(string command, string worktreePath);
}
