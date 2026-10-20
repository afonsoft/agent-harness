using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Application.Agents;

/// <summary>
/// SPEC-20261020-agent-attention-model RF-005/RF-006: per-session ≤4KB tail
/// buffer fed from PTY/stdout chunks. Extracts OSC 0/2 titles and
/// pattern-matches the ANSI-stripped tail through
/// <see cref="AgentCliAttentionPatterns"/>; publishes weak signals into the
/// evaluator debounced to ≥<see cref="DebounceMs"/> per session so a burst
/// of output produces at most one evaluation.
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
        public DateTime LastPublishUtc;
        public string? LastTitle;
    }

    private readonly IAgentAttentionService _attention;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, SessionBuffer> _buffers = new(StringComparer.Ordinal);

    public AgentOutputAttentionService(
        IAgentAttentionService attention, TimeProvider? timeProvider = null)
    {
        _attention = attention;
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

            var kind = AgentCliAttentionPatterns.KindForBinary(cli)
                ?? AgentCliKind.Claude; // unknown CLI → generic table (kind only picks the table)
            var cliName = kind.ToString().ToLowerInvariant();

            // RF-006: OSC title — known titles are a weak signal; unknown
            // titles still count as an "alive" hint via the Working publish.
            var osc = OscTitle.Match(buffer.Tail.ToString());
            var tail = Ansi.Replace(buffer.Tail.ToString(), "");
            var matched = AgentCliAttentionPatterns.Match(kind, tail);

            AgentAttentionState suggested;
            AttentionSignalKind signalKind;
            string? reason, evidence;
            if (matched is not null)
            {
                suggested = AgentAttentionState.WaitingYou;
                signalKind = AttentionSignalKind.OutputPattern;
                reason = matched.Kind.ToString().ToLowerInvariant();
                evidence = Truncate(tail.TrimEnd());
            }
            else if (osc.Success && osc.Groups[1].Value != buffer.LastTitle)
            {
                buffer.LastTitle = osc.Groups[1].Value;
                suggested = AgentAttentionState.Working;
                signalKind = AttentionSignalKind.Osc;
                reason = "osc-title";
                evidence = Truncate(osc.Groups[1].Value);
            }
            else
            {
                suggested = AgentAttentionState.Working;
                signalKind = AttentionSignalKind.OutputPattern;
                reason = "output-alive";
                evidence = null;
            }

            _ = _attention.PublishAsync(new AttentionSignal(
                sessionKey, cliName, signalKind, suggested, reason, evidence));
        }
    }

    /// <summary>Drops a session's buffer when the session closes.</summary>
    public void Discard(string sessionKey) => _buffers.TryRemove(sessionKey, out _);

    private static void Append(SessionBuffer buffer, string chunk)
    {
        buffer.Tail.Append(chunk);
        if (buffer.Tail.Length > TailBytes)
        {
            buffer.Tail.Remove(0, buffer.Tail.Length - TailBytes);
        }
    }

    private static string Truncate(string text) =>
        text.Length <= EvidenceChars ? text : text[^EvidenceChars..];
}
