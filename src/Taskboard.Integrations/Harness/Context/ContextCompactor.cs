using System.Text;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;

namespace Taskboard.Integrations.Harness.Context;

/// <summary>
/// <see cref="IContextCompactor"/> — snip → collapse → summarize pipeline
/// (SPEC-20260919-harness-context-memory RF-003). Preserves system prompts
/// and the latest user instruction; deterministic local synthesis (no LLM).
/// </summary>
public sealed class ContextCompactor : IContextCompactor
{
    private const double TriggerRatio = 0.80;
    private const int KeepLastTurns = 5;

    public Task<CompactionResultDto> CompactIfNeededAsync(
        IReadOnlyList<ContextMessageDto> messages,
        int maxTokens,
        CancellationToken cancellationToken = default)
    {
        var normalized = messages
            .Select(m => m.EstimatedTokens > 0 ? m : m with { EstimatedTokens = EstimateTokens(m.Content.Length) })
            .ToList();

        var original = normalized.Sum(m => m.EstimatedTokens);
        if (original <= maxTokens * TriggerRatio)
        {
            return Task.FromResult(new CompactionResultDto(normalized, original, original, "None"));
        }

        // Preserve: leading system messages + the tail starting at the last
        // user message (bounded by KeepLastTurns turns before it).
        var lastUserIndex = FindLastUserIndex(normalized);
        if (lastUserIndex < 0)
        {
            // No user instruction to preserve — nothing safe to compact.
            return Task.FromResult(new CompactionResultDto(normalized, original, original, "None"));
        }

        var systemCount = 0;
        while (systemCount < normalized.Count && normalized[systemCount].Role == "system")
        {
            systemCount++;
        }

        var keepFrom = Math.Max(systemCount, lastUserIndex - (KeepLastTurns - 1));
        var middle = normalized.Skip(systemCount).Take(keepFrom - systemCount).ToList();

        // Nada entre o system e a cauda preservada → nada para condensar.
        if (middle.Count == 0)
        {
            return Task.FromResult(new CompactionResultDto(normalized, original, original, "None"));
        }

        // RF-003: sumariza o trecho antigo (snip/collapse aplicados na síntese).
        var summary = BuildSummary(middle);
        var compacted = normalized.Take(systemCount)
            .Append(summary)
            .Concat(normalized.Skip(keepFrom))
            .ToList();

        var compactedTokens = compacted.Sum(m => m.EstimatedTokens);
        return Task.FromResult(new CompactionResultDto(compacted, original, compactedTokens, "Summarize"));
    }

    private static int FindLastUserIndex(IReadOnlyList<ContextMessageDto> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role == "user")
            {
                return i;
            }
        }

        return -1;
    }

    private static ContextMessageDto BuildSummary(IReadOnlyList<ContextMessageDto> oldMessages)
    {
        var builder = new StringBuilder();
        builder.Append("[summary] Earlier conversation (");
        builder.Append(oldMessages.Count);
        builder.Append(" turns condensed): ");

        var firstUser = oldMessages.FirstOrDefault(m => m.Role == "user");
        if (firstUser is not null)
        {
            var firstLine = firstUser.Content.Split('\n', 2)[0];
            builder.Append("initial request: \"")
                .Append(firstLine.Length > 160 ? firstLine[..160] + "…" : firstLine)
                .Append("\". ");
        }

        var toolCalls = oldMessages.Count(m => m.Role == "tool");
        if (toolCalls > 0)
        {
            builder.Append(toolCalls).Append(" tool outputs processed. ");
        }

        var content = builder.ToString().TrimEnd();
        return new ContextMessageDto("system", content, EstimateTokens(content.Length));
    }

    internal static int EstimateTokens(int chars) => Math.Max(1, chars / 4);
}
