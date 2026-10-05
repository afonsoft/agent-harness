using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;

namespace Taskboard.Application.Chat;

/// <summary>Sequenced envelope on a run's live stream (SPEC-20261005-chat-background-resume RF-003).</summary>
public sealed record ChatRunEventEnvelope(long Seq, ChatStreamEvent Event);

/// <summary>Live in-flight state of a run — the checkpoint the attach stream replays.</summary>
public sealed record ChatRunLiveState(string? Partial, string? PartialReasoning, long LastSeq);

/// <summary>
/// SPEC-20261005-chat-background-resume RF-003: singleton fan-out for detached
/// chat runs. The dispatcher publishes every emitted <see cref="ChatStreamEvent"/>
/// under a per-run monotonically increasing sequence number; subscribers get a
/// channel reader and replay only events newer than the <c>chat.sync</c>
/// checkpoint they already received. The broadcaster also accumulates the
/// in-flight partial content (deltas append, <see cref="ChatPersistedEvent"/>
/// clears) so <see cref="GetLive"/> is a gap-free snapshot: deltas published
/// before a subscribe are in <see cref="ChatRunLiveState.Partial"/>, deltas
/// after it arrive on the channel.
/// </summary>
public sealed class ChatRunBroadcaster
{
    private const int MaxFinishedEntries = 512;

    private sealed class RunState
    {
        public long Seq;
        public readonly StringBuilder Partial = new();
        public readonly StringBuilder PartialReasoning = new();
        public readonly List<ChannelWriter<ChatRunEventEnvelope>> Subscribers = [];
    }

    private readonly ConcurrentDictionary<string, RunState> _live = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _finished = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _finishedOrder = new();

    /// <summary>
    /// Assigns the next sequence number, updates the partial checkpoint and
    /// offers the event to every live subscriber. Returns the assigned seq.
    /// </summary>
    public long Publish(string runId, ChatStreamEvent chatEvent)
    {
        var state = _live.GetOrAdd(runId, _ => new RunState());
        lock (state)
        {
            state.Seq++;
            switch (chatEvent)
            {
                case ChatDeltaEvent delta:
                    state.Partial.Append(delta.Content);
                    break;
                case ChatReasoningEvent reasoning:
                    state.PartialReasoning.Append(reasoning.Content);
                    break;
                case ChatPersistedEvent:
                    state.Partial.Clear();
                    state.PartialReasoning.Clear();
                    break;
            }

            var envelope = new ChatRunEventEnvelope(state.Seq, chatEvent);
            foreach (var writer in state.Subscribers)
            {
                writer.TryWrite(envelope);
            }

            return state.Seq;
        }
    }

    /// <summary>
    /// Registers a subscriber and returns its channel reader. The reader sees
    /// only events published after the call — pair it with
    /// <see cref="GetLive"/> (taken afterwards) for a complete picture.
    /// A run that already finished returns a closed channel.
    /// </summary>
    public ChannelReader<ChatRunEventEnvelope> Subscribe(string runId)
    {
        var channel = Channel.CreateUnbounded<ChatRunEventEnvelope>();
        if (_finished.ContainsKey(runId))
        {
            channel.Writer.TryComplete();
            return channel.Reader;
        }

        var state = _live.GetOrAdd(runId, _ => new RunState());
        lock (state)
        {
            state.Subscribers.Add(channel.Writer);
            // Complete raced with this subscribe — close instead of leaking.
            if (_finished.ContainsKey(runId))
            {
                state.Subscribers.Remove(channel.Writer);
                channel.Writer.TryComplete();
            }
        }

        return channel.Reader;
    }

    /// <summary>Live checkpoint (partial text + high-water seq) or null when no live state exists.</summary>
    public ChatRunLiveState? GetLive(string runId)
    {
        if (!_live.TryGetValue(runId, out var state))
        {
            return null;
        }

        lock (state)
        {
            return new ChatRunLiveState(
                state.Partial.Length > 0 ? state.Partial.ToString() : null,
                state.PartialReasoning.Length > 0 ? state.PartialReasoning.ToString() : null,
                state.Seq);
        }
    }

    /// <summary>
    /// Ends the run: subscribers' channels complete and the live state is
    /// dropped. A bounded finished-set makes post-finish subscribes close
    /// immediately instead of hanging.
    /// </summary>
    public void Complete(string runId)
    {
        _finished[runId] = 0;
        _finishedOrder.Enqueue(runId);
        while (_finished.Count > MaxFinishedEntries && _finishedOrder.TryDequeue(out var oldest))
        {
            _finished.TryRemove(oldest, out _);
        }

        if (!_live.TryRemove(runId, out var state))
        {
            return;
        }

        lock (state)
        {
            foreach (var writer in state.Subscribers)
            {
                writer.TryComplete();
            }

            state.Subscribers.Clear();
        }
    }
}
