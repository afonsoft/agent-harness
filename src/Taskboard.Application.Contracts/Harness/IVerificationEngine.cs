using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.Harness;

/// <summary>
/// Deterministic self-verification engine — runs format/build/test/coverage
/// inside an agent worktree and produces a structured report plus a feedback
/// prompt for the correction loop (SPEC-20260919-harness-verification-loop).
/// Never modifies code itself (except explicit `dotnet format` enforcement).
/// </summary>
public interface IVerificationEngine
{
    Task<VerificationReportDto> RunAsync(
        VerificationRunRequestDto request,
        CancellationToken cancellationToken = default);
}
