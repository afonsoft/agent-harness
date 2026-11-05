using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Application.Agents;

/// <summary>
/// SPEC-20261104-agent-attention-manifests RF-003/RF-004: per-session ≤4KB
/// tail buffer fed from PTY/stdout chunks. Builds a
/// <see cref="DetectionSnapshot"/> (ANSI-stripped tail + OSC title), evaluates
/// it through the manifest engine and publishes weak signals into the
/// attention evaluator debounced to ≥<see cref="DebounceMs"/> per session.
/// The verdict is recorded in the explain store for <c>agent explain</c>.
/// </summary>
public sealed class AgentOutputAttentionService : IAgentOutputAttentionSink
{
    private const int TailBytes = 4096;
    private const int EvidenceChars = 200;
    private static readonly TimeSpan DebounceMs = TimeSpan.FromSeconds(2);

    // ANSI CSI/OSC sequences — NonBacktracking + timeout for hostile output.
    private static readonly Regex Ansi = new(
        @"\x1b\[[0-9;?]*[ -/]*[@-~]|\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)|\x1b[@-Z\\-_]",
        RegexOptions.Compiled | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(100));

    // OSC 0/1/2 title: ESC ] (0|1|2) ; title (BEL | ESC \).
    private static readonly Regex OscTitle = new(
        @"\x1b\](?:0|1|2);([^\x07\x1b]{1,200})(?:\x07|\x1b\\)",
        RegexOptions.Compiled | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(100));

    private sealed class SessionBuffer
    {
        public readonly StringBuilder Tail = new(TailBytes);
        public readonly StringBuilder Raw = new(TailBytes);
        public DateTime LastPublishUtc;
        public string? LastTitle;
    }

    private readonly IAgentAttentionService _attention;
    private readonly IAgentDetectionEngine _detection;
    private readonly IAgentDetectionExplainStore? _explain;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, SessionBuffer> _buffers = new(StringComparer.Ordinal);

    public AgentOutputAttentionService(
        IAgentAttentionService attention,
        IAgentDetectionEngine detection,
        IAgentDetectionExplainStore? explain = null,
        TimeProvider? timeProvider = null)
    {
        _attention = attention;
        _detection = detection;
        _explain = explain;
        _time = timeProvider ?? TimeProvider.System;
    }

    public void ObserveOutput(string sessionKey, string? cli, string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return;
        }

        var buffer = _buffers.GetOrAdd(sessionKey, _ => new SessionBuffer());
        lock (buffer)
        {
            var now = _time.GetUtcNow().UtcDateTime;
            if (now - buffer.LastPublishUtc < DebounceMs)
            {
                Append(buffer, chunk);
                return;
            }
            buffer.LastPublishUtc = now;
            Append(buffer, chunk);

            var raw = buffer.Raw.ToString();
            var osc = OscTitle.Match(raw);
            var tail = Ansi.Replace(raw, "");
            var title = osc.Success ? osc.Groups[1].Value : buffer.LastTitle;
            var cliName = NormalizeCliName(cli);

            var verdict = _detection.Evaluate(new DetectionSnapshot(tail, title), cliName);
            _explain?.Record(sessionKey, new AgentDetectionExplainDto(
                sessionKey, verdict.SuggestedState, verdict.ManifestId,
                verdict.ManifestSource,
                verdict.MatchedRuleId is null
                    ? null
                    : new EvaluatedRuleDto(
                        verdict.MatchedRuleId, verdict.MatchedRulePriority ?? 0,
                        verdict.MatchedRegion ?? string.Empty, Matched: true, FailedGate: null),
                verdict.EvaluatedRules, verdict.Flags, verdict.Warnings,
                SnapshotHash(tail), now));

            if (verdict.SuggestedState is { } suggested)
            {
                buffer.LastTitle = title;
                _ = _attention.PublishAsync(new AttentionSignal(
                    sessionKey, cliName ?? "unknown",
                    verdict.MatchedRegion is "osc_title" or "osc_progress"
                        ? AttentionSignalKind.Osc
                        : AttentionSignalKind.OutputPattern,
                    suggested, verdict.MatchedRuleId, Truncate(tail.TrimEnd())));
                return;
            }

            if (verdict.MatchedRuleId is not null)
            {
                // skip_state_update / unknown rule matched — suppress the signal.
                buffer.LastTitle = title;
                return;
            }

            // No manifest rule matched: a new OSC title is an "alive" hint;
            // otherwise plain output still counts as Working.
            if (osc.Success && title != buffer.LastTitle)
            {
                buffer.LastTitle = title;
                _ = _attention.PublishAsync(new AttentionSignal(
                    sessionKey, cliName ?? "unknown", AttentionSignalKind.Osc,
                    AgentAttentionState.Working, "osc-title", Truncate(title ?? "")));
                return;
            }

            buffer.LastTitle = title;
            _ = _attention.PublishAsync(new AttentionSignal(
                sessionKey, cliName ?? "unknown", AttentionSignalKind.OutputPattern,
                AgentAttentionState.Working, "output-alive", null));
        }
    }

    /// <summary>Drops a session's buffer + explain entry when the session closes.</summary>
    public void Discard(string sessionKey)
    {
        _buffers.TryRemove(sessionKey, out _);
        _explain?.Discard(sessionKey);
    }

    private static string? NormalizeCliName(string? cli)
    {
        if (string.IsNullOrWhiteSpace(cli))
        {
            return null;
        }
        var name = Path.GetFileName(cli.Trim())
            .Replace(".exe", "", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();
        return name.Length == 0 ? null : name;
    }

    private static void Append(SessionBuffer buffer, string chunk)
    {
        buffer.Tail.Append(chunk);
        if (buffer.Tail.Length > TailBytes)
        {
            buffer.Tail.Remove(0, buffer.Tail.Length - TailBytes);
        }
        buffer.Raw.Append(chunk);
        if (buffer.Raw.Length > TailBytes * 2)
        {
            buffer.Raw.Remove(0, buffer.Raw.Length - TailBytes * 2);
        }
    }

    private static string SnapshotHash(string tail)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(tail));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    private static string Truncate(string text) =>
        text.Length <= EvidenceChars ? text : text[^EvidenceChars..];
}
