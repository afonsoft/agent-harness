using System.Collections.Concurrent;

namespace Taskboard.Application.Chat;

/// <summary>
/// SPEC-20261001-pr-review-backlog-fixes B-01: shared registry of in-flight
/// chat runs. <see cref="ChatService"/> is scoped, so the send and stop
/// endpoints used to see different dictionaries — a stop always returned 409.
/// The singleton coordinator makes cancellation reach the running stream.
/// </summary>
public sealed class ChatRunCoordinator
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runs = new();

    /// <summary>
    /// Registers a new run for <paramref name="conversationId"/>, cancelling
    /// and discarding any previous one for the same conversation.
    /// </summary>
    public async Task<CancellationTokenSource> BeginAsync(string conversationId, CancellationToken requestAborted)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        if (_runs.TryRemove(conversationId, out var previous))
        {
            await previous.CancelAsync().ConfigureAwait(false);
            previous.Dispose();
        }

        _runs[conversationId] = cts;
        return cts;
    }

    /// <summary>Cancels the run for <paramref name="conversationId"/>; false when none is running.</summary>
    public bool Stop(string conversationId)
    {
        if (_runs.TryRemove(conversationId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Removes the run only if it is still the registered one — a newer send
    /// for the same conversation must not lose its entry.
    /// </summary>
    public void End(string conversationId, CancellationTokenSource cts) =>
        _runs.TryRemove(new KeyValuePair<string, CancellationTokenSource>(conversationId, cts));
}
