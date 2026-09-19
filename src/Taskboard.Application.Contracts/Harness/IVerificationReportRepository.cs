using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.Harness;

/// <summary>
/// Persists verification evidence for post-mortem / human review
/// (SPEC-20260919-harness-verification-loop RF-005).
/// </summary>
public interface IVerificationReportRepository
{
    Task SaveAsync(
        string worktreePath,
        VerificationReportDto report,
        int attempts,
        CancellationToken cancellationToken = default);
}
