using Microsoft.Extensions.Configuration;

namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// SPEC-20261005-chat-context-management RF-009: context pressure/compaction
/// configuration — one place for the catalog keys and their effective values.
/// </summary>
public static class ChatContextBudget
{
    public const string EnabledKey = "Taskboard:Chat:Context:Enabled";
    public const string CompactAtTokensKey = "Taskboard:Chat:Context:CompactAtTokens";
    public const string MaxTokensKey = "Taskboard:Chat:Context:MaxTokens";
    public const string CompactRatioKey = "Taskboard:Chat:Context:CompactRatio";
    public const string MaxContextTokensKey = "Taskboard:Chat:Context:MaxContextTokens";
    public const string KeepRecentTurnsKey = "Taskboard:Chat:Context:KeepRecentTurns";
    public const string SpillBytesKey = "Taskboard:Chat:Context:SpillBytes";
    public const string SpillHeadBytesKey = "Taskboard:Chat:Context:SpillHeadBytes";
    public const string SummaryMaxTokensKey = "Taskboard:Chat:Context:SummaryMaxTokens";
    public const string SummaryPromptKey = "Taskboard:Chat:Context:SummaryPrompt";

    /// <summary>RF-004: system prompt for the non-tool summarizer call.</summary>
    public const string DefaultSummaryPrompt =
        "Summarize the earlier conversation turns for an agent continuing the work: "
        + "goals, decisions made, tools used and their outcomes, files touched, "
        + "and anything still pending. Concise markdown bullets; no preamble.";

    /// <summary>RNF-001: tombstone replacing a pruned wire tool result.</summary>
    public const string PrunedTombstone =
        "[earlier tool output pruned during context compaction — content not sent]";

    /// <summary>Open question #2: the model is told compaction happened.</summary>
    public const string CompactedNote =
        "[context compacted — older tool outputs were pruned or spilled; summary of earlier turns follows]";

    public static bool Enabled(IConfiguration configuration) =>
        configuration[EnabledKey] is not { } raw || (bool.TryParse(raw, out var on) ? on : true);

    /// <summary>RF-009: explicit compaction threshold; 0 = derive from budget.</summary>
    public static int CompactAtTokens(IConfiguration configuration) =>
        ReadInt(configuration, CompactAtTokensKey, 0);

    /// <summary>RF-009: absolute wire budget cap; 0 = ratio of provider context.</summary>
    public static int MaxTokens(IConfiguration configuration) =>
        ReadInt(configuration, MaxTokensKey, 0);

    /// <summary>Open question #1: fraction of provider context to compact at.</summary>
    public static double CompactRatio(IConfiguration configuration) =>
        configuration[CompactRatioKey] is { } raw
        && double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var ratio)
        && ratio is > 0 and <= 1
            ? ratio
            : 0.8;

    /// <summary>Provider context window used when MaxTokens is unset.</summary>
    public static int MaxContextTokens(IConfiguration configuration) =>
        ReadInt(configuration, MaxContextTokensKey, 128000);

    /// <summary>RF-003: tool results older than the last N user turns get pruned.</summary>
    public static int KeepRecentTurns(IConfiguration configuration) =>
        ReadInt(configuration, KeepRecentTurnsKey, 6);

    /// <summary>RF-006: tool results larger than this spill to disk.</summary>
    public static int SpillBytes(IConfiguration configuration) =>
        ReadInt(configuration, SpillBytesKey, 32768);

    /// <summary>RF-006: bytes of the spilled result kept inline on the wire.</summary>
    public static int SpillHeadBytes(IConfiguration configuration) =>
        ReadInt(configuration, SpillHeadBytesKey, 8192);

    /// <summary>RF-004: max_tokens for the summarizer call.</summary>
    public static int SummaryMaxTokens(IConfiguration configuration) =>
        ReadInt(configuration, SummaryMaxTokensKey, 1200);

    private static int ReadInt(IConfiguration configuration, string key, int fallback) =>
        configuration[key] is { } raw && int.TryParse(raw, out var n) ? n : fallback;

    public static string SummaryPrompt(IConfiguration configuration) =>
        configuration[SummaryPromptKey] is { Length: > 0 } configured
            ? configured
            : DefaultSummaryPrompt;

    /// <summary>
    /// Effective compaction threshold: absolute MaxTokens, else explicit
    /// CompactAtTokens, else provider MaxContextTokens × CompactRatio.
    /// </summary>
    public static int CompactLimit(IConfiguration configuration)
    {
        if (MaxTokens(configuration) is > 0 and var max)
        {
            return max;
        }

        if (CompactAtTokens(configuration) is > 0 and var at)
        {
            return at;
        }

        return (int)(MaxContextTokens(configuration) * CompactRatio(configuration));
    }
}
