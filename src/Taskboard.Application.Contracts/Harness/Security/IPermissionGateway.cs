using Taskboard.Dtos;
using Taskboard.Harness;

namespace Taskboard.Application.Contracts.Harness;

/// <summary>
/// Runtime permission gateway — classifies tool/process calls, enforces the
/// path jail and decides allow/deny/approval per the run's sandbox policy
/// (SPEC-20260919-harness-security-permission-gateway RF-001..RF-004).
/// </summary>
public interface IPermissionGateway
{
    /// <summary>
    /// Evaluates a tool invocation before dispatch. Throws
    /// <see cref="SecurityAccessDeniedException"/> when the command touches
    /// paths outside <paramref name="worktreePath"/>.
    /// </summary>
    Task<SecurityEvaluationDto> EvaluateAsync(
        string toolName,
        string command,
        string worktreePath,
        SecurityPolicyMode policy = SecurityPolicyMode.Standard,
        CancellationToken cancellationToken = default);

    /// <summary>Scrubs known secret patterns from output streams (RF-003).</summary>
    string ScrubSecrets(string output);
}
