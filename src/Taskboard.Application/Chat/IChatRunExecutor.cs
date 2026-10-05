using Taskboard.Domain.Entities.Chat;

namespace Taskboard.Application.Chat;

/// <summary>
/// SPEC-20261005-chat-background-resume RF-002: the detached run executor —
/// implemented by <see cref="ChatService"/> and driven by the hosted
/// dispatcher inside a dispatcher-owned scope. Kept behind an interface so
/// dispatcher tests substitute a fake.
/// </summary>
public interface IChatRunExecutor
{
    /// <summary>
    /// Runs one turn: the same tool loop the old request-bound stream had —
    /// transcript up to the run's trigger message, per-iteration persistence,
    /// deltas/events yielded live. <paramref name="runCts"/> is cancelled by a
    /// user stop; <paramref name="stoppingToken"/> is the host-lifetime token
    /// used for post-stop persistence.
    /// </summary>
    IAsyncEnumerable<ChatStreamEvent> ExecuteAsync(
        ChatRun run, CancellationTokenSource runCts, CancellationToken stoppingToken);
}
