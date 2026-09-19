using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.Harness;

/// <summary>
/// Closed correction loop (SPEC-20260919-harness-verification-loop RF-004/RF-005):
/// verifies, feeds the failure prompt back into the agent via
/// <paramref name="retryAsync"/>, and escalates to a human after
/// <c>MaxAttempts</c> (default 2 retries).
/// </summary>
public interface IVerificationLoop
{
    /// <param name="retryAsync">Re-invokes the agent with the feedback prompt.</param>
    Task<VerificationReportDto> RunAsync(
        VerificationRunRequestDto request,
        Func<string, CancellationToken, Task> retryAsync,
        CancellationToken cancellationToken = default);
}
