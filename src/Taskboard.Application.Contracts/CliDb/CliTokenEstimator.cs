using Taskboard.Application.Contracts.CliMetrics;

namespace Taskboard.Application.Contracts.CliDb;

/// <summary>
/// Token estimation for vendors whose schema exposes no usage counters —
/// derived only from scalars (text lengths, event counts, vendor totals);
/// message/prompt content never leaves the vendor database.
/// SPEC-20260922-finops-cli-usage-breakdown RF-002.
/// </summary>
public sealed class CliTokenEstimator
{
    private readonly int _charsPerToken;
    private readonly long _tokensPerEvent;

    public CliTokenEstimator(CliMetricsOptions? options = null)
    {
        _charsPerToken = Math.Max(1, options?.EstimatedCharsPerToken ?? 4);
        _tokensPerEvent = Math.Max(0, options?.EstimatedTokensPerEvent ?? 1000);
    }

    /// <summary><c>ceil(chars / EstimatedCharsPerToken)</c>; 0 chars → 0 tokens.</summary>
    public long FromChars(long chars) =>
        chars <= 0 ? 0 : (long)Math.Ceiling(chars / (double)_charsPerToken);

    /// <summary><c>events × EstimatedTokensPerEvent</c>.</summary>
    public long FromEvents(long events) => events <= 0 ? 0 : events * _tokensPerEvent;
}
