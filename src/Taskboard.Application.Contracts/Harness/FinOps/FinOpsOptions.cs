namespace Taskboard.Harness.FinOps;

/// <summary>
/// <c>Taskboard:FinOps</c> configuration.
/// SPEC-20260922-finops-dashboard-detail RF-004.
/// </summary>
public sealed class FinOpsOptions
{
    /// <summary>
    /// A session/run counts as <c>running</c> when its last activity is within
    /// this many seconds of now (reference spec ACTIVE_WINDOW_SECS).
    /// </summary>
    public int ActiveWindowSeconds { get; set; } = 1800;
}
