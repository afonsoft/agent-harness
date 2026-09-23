namespace Taskboard.Harness;

/// <summary>
/// <c>Taskboard:Pipelines:AutoRetry</c> — per-stage automatic retry policy
/// (SPEC-20260923-cockpit-run-hardening RF-002): N failures per bound agent
/// spaced by <see cref="Interval"/>, then rotation to the next untried
/// eligible CLI, then terminal <see cref="PipelineStatus.Failed"/>.
/// </summary>
public sealed class PipelineAutoRetryOptions
{
    /// <summary>Master switch — false restores the manual-retry-only behaviour.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Failures tolerated per bound agent before rotation/exhaustion.</summary>
    public int AttemptsPerAgent { get; set; } = 5;

    /// <summary>Delay between a stage failure and its next auto-retry dispatch.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
}
