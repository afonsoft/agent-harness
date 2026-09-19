namespace Taskboard.Dtos;

/// <summary>Structured compiler diagnostic extracted from `dotnet build` output (RF-001).</summary>
public sealed record CompilationErrorDto(
    string File,
    int Line,
    int Column,
    string ErrorCode,
    string Message);

/// <summary>A single failed test (RF-002).</summary>
public sealed record TestFailureDto(
    string Name,
    string Message,
    string? StackTrace);

/// <summary>Test run summary (RF-002).</summary>
public sealed record TestSummaryDto(
    int Total,
    int Passed,
    int Failed,
    IReadOnlyList<TestFailureDto> Failures);

/// <summary>Payload for <c>POST /api/harness/verification/run</c> (SPEC §5).</summary>
public sealed record VerificationRunRequestDto(
    string WorktreePath,
    string SolutionFile,
    double MinCoverageThreshold,
    bool EnforceFormat = false,
    int MaxAttempts = 1);

/// <summary>
/// Structured verification report (SPEC §5). <see cref="FeedbackPrompt"/> is the
/// markdown payload reinjected into the agent on failure (RF-004).
/// </summary>
public sealed record VerificationReportDto(
    bool IsSuccess,
    string Status,
    IReadOnlyList<CompilationErrorDto> CompilationErrors,
    TestSummaryDto? TestSummary,
    double CoveragePercent,
    string? FeedbackPrompt);
