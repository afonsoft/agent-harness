using System.Collections.Concurrent;

namespace Taskboard.Application.Chat;

/// <summary>The outcome a waiting tool call should apply (SPEC-20261005-chat-tool-approval).</summary>
public enum ChatApprovalVerdict
{
    /// <summary>Execute this call only.</summary>
    Allowed,

    /// <summary>Synthetic denial result — the loop continues.</summary>
    Denied,
}

/// <summary>
/// SPEC-20261005-chat-tool-approval RF-002/RF-003: singleton registry of
/// pending approval waits. The executor registers a
/// <see cref="TaskCompletionSource{T}"/> per pending <c>ChatApproval</c>; the
/// decide endpoint (a different DI scope) completes it — the suspended call
/// resumes without holding a DB transaction or blocking the dispatcher
/// (RNF-001). The database row stays the source of truth: the TCS only
/// carries the wake-up signal plus the verdict.
/// </summary>
public sealed class ChatApprovalCoordinator
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ChatApprovalVerdict>> _pending = new(StringComparer.Ordinal);

    /// <summary>Registers a wait for <paramref name="approvalId"/>. Caller owns TryUnregister.</summary>
    public TaskCompletionSource<ChatApprovalVerdict> Register(string approvalId)
    {
        var tcs = new TaskCompletionSource<ChatApprovalVerdict>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[approvalId] = tcs;
        return tcs;
    }

    /// <summary>Completes the wait; false when no live waiter exists (e.g. run already cancelled).</summary>
    public bool Resolve(string approvalId, ChatApprovalVerdict verdict)
    {
        if (!_pending.TryGetValue(approvalId, out var tcs))
        {
            return false;
        }

        tcs.TrySetResult(verdict);
        return true;
    }

    /// <summary>Drops the registration once the wait ends — idempotent.</summary>
    public void Unregister(string approvalId) =>
        _pending.TryRemove(approvalId, out _);

    /// <summary>True while a waiter is registered for <paramref name="approvalId"/>.</summary>
    public bool IsWaiting(string approvalId) => _pending.ContainsKey(approvalId);
}
