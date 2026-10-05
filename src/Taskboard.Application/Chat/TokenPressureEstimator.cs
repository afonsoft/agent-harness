using System.Text;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Application.Chat;

/// <summary>
/// SPEC-20261005-chat-context-management RF-001/RNF-004: heuristic wire-token
/// estimator — no tokenizer lib, O(messages) per call.
/// tokens/message = ceil(contentBytes/4) + 16 per tool call + 1000 per image
/// marker; a provider-reported prompt_tokens anchors the estimate (baseline
/// pattern: anchor + delta growth on the messages appended since).
/// </summary>
public static class TokenPressureEstimator
{
    public const int ToolCallOverheadTokens = 16;
    public const int ImageMarkerTokens = 1000;
    private const int MessageFramingTokens = 4;

    public static int EstimateTokens(IReadOnlyList<OpenAiChatMessage> wire)
    {
        var tokens = 0;
        foreach (var message in wire)
        {
            tokens += EstimateMessage(message);
        }

        return tokens;
    }

    /// <summary>
    /// RF-001 anchor pattern: the provider's last reported prompt_tokens covered
    /// the wire at <paramref name="anchorCount"/> entries — estimate only the
    /// delta appended since and add it on top.
    /// </summary>
    public static int AnchoredEstimate(
        IReadOnlyList<OpenAiChatMessage> wire, int anchorTokens, int anchorCount)
    {
        if (anchorCount <= 0 || anchorCount >= wire.Count)
        {
            return anchorCount >= wire.Count ? anchorTokens : EstimateTokens(wire);
        }

        var delta = 0;
        for (var i = anchorCount; i < wire.Count; i++)
        {
            delta += EstimateMessage(wire[i]);
        }

        return anchorTokens + delta;
    }

    private static int EstimateMessage(OpenAiChatMessage message)
    {
        var tokens = MessageFramingTokens;
        if (message.Content is { Length: > 0 } content)
        {
            tokens += ByteTokens(content);
            // Wire never carries binaries; "[image]" markers proxy image cost.
            tokens += CountOccurrences(content, "[image]") * ImageMarkerTokens;
        }

        if (message.ToolCalls is { Count: > 0 } toolCalls)
        {
            tokens += toolCalls.Count * ToolCallOverheadTokens;
            foreach (var call in toolCalls)
            {
                if (call.ArgumentsJson is { Length: > 0 } args)
                {
                    tokens += ByteTokens(args);
                }
            }
        }

        return tokens;
    }

    private static int ByteTokens(string text) => (Encoding.UTF8.GetByteCount(text) + 3) / 4;

    private static int CountOccurrences(string text, string marker)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += marker.Length;
        }

        return count;
    }
}
