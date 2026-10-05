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

    /// <summary>
    /// SPEC-20261005-chat-tool-approval RF-002: a run parked on a pending
    /// approval — hub/push fan-out so a closed tab still learns the run needs
    /// a human. Default no-op keeps custom notifiers compiling.
    /// </summary>
    Task ApprovalAskedAsync(
        ChatRun run, string approvalId, string toolName, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <summary>
    /// SPEC-20261005-chat-context-management RF-007: per-estimation pressure
    /// sample — the sidebar badge tracks this while a run is live.
    /// </summary>
    Task RunPressureAsync(
        string runId, string conversationId, int estimatedTokens, int limit,
        bool compacted, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
