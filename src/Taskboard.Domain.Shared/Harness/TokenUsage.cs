namespace Taskboard.Harness;

/// <summary>
/// Token usage reported by an agent CLI/API for a run or stage
/// (SPEC-20260919-ade-observability-finops RF-001).
/// </summary>
public sealed record TokenUsage(long InputTokens, long OutputTokens, long CacheWriteTokens, long CacheReadTokens)
{
    public static readonly TokenUsage Zero = new(0, 0, 0, 0);

    public long TotalTokens => InputTokens + OutputTokens + CacheWriteTokens + CacheReadTokens;

    public long CacheTokens => CacheWriteTokens + CacheReadTokens;

    public TokenUsage Add(TokenUsage other) =>
        new(
            InputTokens + other.InputTokens,
            OutputTokens + other.OutputTokens,
            CacheWriteTokens + other.CacheWriteTokens,
            CacheReadTokens + other.CacheReadTokens);
}
