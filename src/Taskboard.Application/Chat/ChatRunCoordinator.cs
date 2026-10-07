using System.Collections.Concurrent;

namespace Taskboard.Application.Chat;

/// <summary>
/// SPEC-20261001-pr-review-backlog-fixes B-01: shared registry of in-flight
/// chat runs. <see cref="ChatService"/> is scoped, so the send and stop
/// endpoints used to see different dictionaries — a stop always returned 409.
/// The singleton coordinator makes cancellation reach the running stream.
/// SPEC-20261012-chat-run-controls: also owns the pause-request flags, the
/// resume waiters a parked executor blocks on, and the per-run activity
/// stamps the client-side stalled derivation reads off chat.sync.
/// </summary>
public sealed class ChatRunCoordinator
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runs = new();
    private readonly ConcurrentDictionary<string, byte> _pauseRequests = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _resumeWaiters = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastActivityUtc = new();

    /// <summary>
    /// Registers a new run for <paramref name="conversationId"/>, cancelling
    /// and discarding any previous one for the same conversation.
    /// SPEC-20261005-chat-background-resume: the token is detached — no
    /// requestAborted in its chain, so closing the browser never cancels a run.
    /// </summary>
    public async Task<CancellationTokenSource> BeginAsync(string conversationId)
    {
        var cts = new CancellationTokenSource();
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

    /// <summary>True while a run for <paramref name="conversationId"/> is registered.</summary>
    public bool IsRunning(string conversationId) => _runs.ContainsKey(conversationId);

    /// <summary>
    /// Removes the run only if it is still the registered one — a newer send
    /// for the same conversation must not lose its entry.
    /// </summary>
    public void End(string conversationId, CancellationTokenSource cts) =>
        _runs.TryRemove(new KeyValuePair<string, CancellationTokenSource>(conversationId, cts));

    // ---- SPEC-20261012: pause flag + resume waiters (keyed by runId) ----

    /// <summary>Endpoint-side: asks the live executor to park at the next boundary.</summary>
    public void RequestPause(string runId) => _pauseRequests[runId] = 1;

    /// <summary>Executor-side: consumes the pause flag once — the boundary check.</summary>
    public bool ConsumePauseRequest(string runId) => _pauseRequests.TryRemove(runId, out _);

    /// <summary>True while a pause request is pending for <paramref name="runId"/>.</summary>
    public bool IsPauseRequested(string runId) => _pauseRequests.ContainsKey(runId);

    /// <summary>
    /// Executor-side: registers the resume waiter BEFORE the pause event
    /// flushes. The registration must precede <c>chat.paused</c> — a resume
    /// endpoint arriving mid-flush then still sees <see cref="IsParked"/> and
    /// completes this waiter instead of requeueing a run whose executor is
    /// about to park (double-drive).
    /// </summary>
    public TaskCompletionSource RegisterResumeWaiter(string runId) =>
        _resumeWaiters.GetOrAdd(
            runId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    /// <summary>
    /// Drops this executor's waiter when it leaves the park un-signaled
    /// (user stop / shutdown). Only removes while the stored waiter is still
    /// this instance — a <see cref="SignalResume"/>-removed entry is a no-op.
    /// </summary>
    public void AbandonResumeWaiter(string runId, TaskCompletionSource waiter) =>
        _resumeWaiters.TryRemove(
            new KeyValuePair<string, TaskCompletionSource>(runId, waiter));

    /// <summary>
    /// Parks the executor until <see cref="SignalResume"/> fires or the linked
    /// token cancels (user stop / shutdown). Throws OperationCanceledException
    /// on cancel — callers decide whether it was a stop or an interrupt.
    /// </summary>
    public Task WaitResumeAsync(string runId, CancellationToken ct)
    {
        var waiter = RegisterResumeWaiter(runId);
        return waiter.Task.WaitAsync(ct);
    }

    /// <summary>
    /// Endpoint-side resume: clears any pending pause flag (so a flag that
    /// never landed can't park later) and releases the parked executor when
    /// one waits. True when a live waiter was released.
    /// </summary>
    public bool SignalResume(string runId)
    {
        _pauseRequests.TryRemove(runId, out _);
        return _resumeWaiters.TryRemove(runId, out var tcs) && tcs.TrySetResult();
    }

    /// <summary>True while an executor is parked on <paramref name="runId"/>.</summary>
    public bool IsParked(string runId) => _resumeWaiters.ContainsKey(runId);

    /// <summary>Dispatcher-side heartbeat — every published event stamps the run.</summary>
    public void Touch(string runId) => _lastActivityUtc[runId] = DateTime.UtcNow;

    /// <summary>Last event stamp for one run; null when it never emitted.</summary>
    public DateTime? GetLastActivity(string runId) =>
        _lastActivityUtc.TryGetValue(runId, out var stamp) ? stamp : null;

    /// <summary>Drops all per-run control state on run end.</summary>
    public void ClearRun(string runId)
    {
        _pauseRequests.TryRemove(runId, out _);
        _resumeWaiters.TryRemove(runId, out _);
        _lastActivityUtc.TryRemove(runId, out _);
    }
}
