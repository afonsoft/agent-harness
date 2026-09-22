namespace Taskboard.Application.Contracts.CliMetrics;

/// <summary>
/// <c>Taskboard:CliMetrics</c> configuration. SPEC-20260919-cli-metrics RF-006/RF-008.
/// </summary>
public sealed class CliMetricsOptions
{
    /// <summary>Master switch — disabled skips background sync and hides the manual endpoint.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Background sync period.</summary>
    public int SyncIntervalMinutes { get; set; } = 15;

    /// <summary>Raw session rows older than this are purged; daily aggregates are kept.</summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>
    /// Chars-per-token divisor for length-based estimates on vendors without
    /// usage columns (SPEC-20260922-finops-cli-usage-breakdown RF-002).
    /// </summary>
    public int EstimatedCharsPerToken { get; set; } = 4;

    /// <summary>Token estimate per vendor event counter (e.g. Claude context-mode events).</summary>
    public int EstimatedTokensPerEvent { get; set; } = 1000;
}
