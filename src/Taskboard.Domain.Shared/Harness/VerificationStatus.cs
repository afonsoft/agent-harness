namespace Taskboard.Harness;

/// <summary>
/// Final status of a verification run
/// (SPEC-20260919-harness-verification-loop §5/§6).
/// </summary>
public enum VerificationStatus
{
    /// <summary>Format, build, tests and coverage gate all green.</summary>
    Passed,

    /// <summary>`dotnet format --verify-no-changes` reported diffs.</summary>
    FormatFailed,

    /// <summary>Compilation failed — `CompilationErrors` populated.</summary>
    BuildFailed,

    /// <summary>Build passed but one or more tests failed.</summary>
    TestsFailed,

    /// <summary>Tests passed but line coverage below the ratchet threshold.</summary>
    CoverageRegression,

    /// <summary>Test step timed out (default 120s per SPEC edge cases).</summary>
    TestTimeout,

    /// <summary>Correction retries exhausted — human intervention required (RF-005).</summary>
    EscalatedToHuman
}
