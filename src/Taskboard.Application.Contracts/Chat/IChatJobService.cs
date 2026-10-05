namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// Process registry for chat background jobs (SPEC-20261005-chat-jobs-schedule-search
/// RF-001/RF-002). Implemented as a server singleton — tools and endpoints
/// share the same live-process table; output streams to per-job files under
/// the data dir and completion writes a system note to the transcript.
/// </summary>
public interface IChatJobService
{
    /// <summary>
    /// Registers + spawns the command (the caller already ran the security
    /// gateway). Returns the live row — execution is detached.
    /// </summary>
    Task<ChatJobDto> StartAsync(
        string conversationId, string? runId, string command, string workspacePath,
        CancellationToken cancellationToken = default);

    /// <summary>Conversation-scoped list; <paramref name="activeOnly"/> = running jobs.</summary>
    Task<IReadOnlyList<ChatJobDto>> ListAsync(
        string conversationId, bool activeOnly, CancellationToken cancellationToken = default);

    /// <summary>Tail of the captured output (running or finished), <c>null</c> when unknown.</summary>
    Task<ChatJobOutputDto?> GetOutputAsync(
        string conversationId, string jobId, int tailBytes, CancellationToken cancellationToken = default);

    /// <summary>SIGTERM → grace → SIGKILL. <c>null</c> when unknown; 409-shape via
    /// <see cref="Taskboard.Application.Chat.ChatJobConflictException"/> when already terminal.</summary>
    Task<ChatJobDto?> KillAsync(
        string conversationId, string jobId, CancellationToken cancellationToken = default);
}
