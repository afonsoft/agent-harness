using Taskboard.Domain.Entities.Chat;

namespace Taskboard.Application.Chat;

/// <summary>
/// SPEC-20261005-chat-background-resume RF-008 seam: the dispatcher reports a
/// run's terminal transition here. No implementation is registered until P2
/// (ChatRunHub + notification prefs) — resolve optionally, never required.
/// </summary>
public interface IChatRunNotifier
{
    /// <summary>Called once per run when it reaches a terminal status.</summary>
    Task RunCompletedAsync(ChatRun run, CancellationToken cancellationToken);
}
