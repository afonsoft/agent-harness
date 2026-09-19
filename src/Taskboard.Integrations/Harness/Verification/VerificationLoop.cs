using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;
using Taskboard.Harness;

namespace Taskboard.Integrations.Harness.Verification;

/// <inheritdoc cref="IVerificationLoop"/>
public sealed class VerificationLoop : IVerificationLoop
{
    private readonly IVerificationEngine _engine;
    private readonly IVerificationReportRepository _reports;

    public VerificationLoop(
        IVerificationEngine engine,
        IVerificationReportRepository reports)
    {
        _engine = engine;
        _reports = reports;
    }

    public async Task<VerificationReportDto> RunAsync(
        VerificationRunRequestDto request,
        Func<string, CancellationToken, Task> retryAsync,
        CancellationToken cancellationToken = default)
    {
        var maxAttempts = Math.Max(1, request.MaxAttempts);
        VerificationReportDto? last = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var current = request with { Attempt = attempt, MaxAttempts = maxAttempts };
            last = await _engine.RunAsync(current, cancellationToken).ConfigureAwait(false);
            await _reports.SaveAsync(request.WorktreePath, last, attempt, cancellationToken)
                .ConfigureAwait(false);

            if (last.IsSuccess)
            {
                return last;
            }

            if (attempt < maxAttempts && last.FeedbackPrompt is { } prompt)
            {
                await retryAsync(prompt, cancellationToken).ConfigureAwait(false);
            }
        }

        // RF-005: retries esgotados — escala para intervenção humana.
        var escalated = last! with { Status = nameof(VerificationStatus.EscalatedToHuman) };
        await _reports.SaveAsync(request.WorktreePath, escalated, maxAttempts, cancellationToken)
            .ConfigureAwait(false);
        return escalated;
    }
}
