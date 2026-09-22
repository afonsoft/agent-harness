namespace Taskboard.Server.Services;

/// <summary>
/// Per-thread FIFO of queued prompt event ids with serialized dispatch
/// (SPEC-20260921-ai-code-chat-ux RF-002/NFR-002): at most one
/// <c>session/prompt</c> turn is in flight; the next queued item is dispatched
/// only when the current turn completes (idle).
/// </summary>
public sealed class PromptQueue
{
    private readonly object _gate = new();
    private readonly Queue<string> _pending = new();
    private bool _turnActive;

    /// <summary>Event ids still waiting for dispatch, FIFO order.</summary>
    public IReadOnlyList<string> Pending
    {
        get { lock (_gate) { return _pending.ToList(); } }
    }

    public int Count
    {
        get { lock (_gate) { return _pending.Count; } }
    }

    public bool TurnActive
    {
        get { lock (_gate) { return _turnActive; } }
    }

    /// <summary>Appends a queued prompt event id.</summary>
    public void Enqueue(string eventId)
    {
        lock (_gate)
        {
            _pending.Enqueue(eventId);
        }
    }

    /// <summary>Removes a queued item (user cancel before dispatch).</summary>
    public bool Remove(string eventId)
    {
        lock (_gate)
        {
            if (!_pending.Contains(eventId))
            {
                return false;
            }

            var remaining = _pending.Where(id => id != eventId).ToList();
            _pending.Clear();
            foreach (var id in remaining)
            {
                _pending.Enqueue(id);
            }

            return true;
        }
    }

    /// <summary>
    /// Atomically dequeues the next item and marks the turn active. Returns
    /// false when a turn is already in flight or the queue is empty — so two
    /// racing callers can never dispatch concurrently.
    /// </summary>
    public bool TryDispatch(out string? eventId)
    {
        lock (_gate)
        {
            if (_turnActive || _pending.Count == 0)
            {
                eventId = null;
                return false;
            }

            _turnActive = true;
            eventId = _pending.Dequeue();
            return true;
        }
    }

    /// <summary>Puts a failed dispatch back at the front and frees the turn.</summary>
    public void DispatchFailed(string eventId)
    {
        lock (_gate)
        {
            _turnActive = false;
            var remaining = new Queue<string>();
            remaining.Enqueue(eventId);
            foreach (var id in _pending)
            {
                remaining.Enqueue(id);
            }

            _pending.Clear();
            foreach (var id in remaining)
            {
                _pending.Enqueue(id);
            }
        }
    }

    /// <summary>Current turn ended (stopReason/idle) — next item may dispatch.</summary>
    public void TurnCompleted()
    {
        lock (_gate)
        {
            _turnActive = false;
        }
    }

    /// <summary>A prompt was sent outside the queue (steer/immediate).</summary>
    public void MarkTurnActive()
    {
        lock (_gate)
        {
            _turnActive = true;
        }
    }

    /// <summary>Session died — frees the turn but keeps pending items for the next session.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _turnActive = false;
        }
    }
}
