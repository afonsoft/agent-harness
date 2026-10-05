namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// Conversation-bound scheduled follow-ups (SPEC-20261005-chat-jobs-schedule-search
/// RF-004/RF-005/RF-006). The hosted dispatcher calls
/// <see cref="DeliverDueAsync"/> on every tick (and once at boot for missed
/// fires); the tools/UI use the CRUD surface — all conversation-scoped.
/// </summary>
public interface IChatScheduleService
{
    Task<IReadOnlyList<ChatScheduleDto>> ListAsync(
        string conversationId, CancellationToken cancellationToken = default);

    Task<ChatScheduleDto> CreateAsync(
        string conversationId, CreateChatScheduleRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Patch title/prompt or activate/deactivate (cancel). <c>null</c> when unknown.</summary>
    Task<ChatScheduleDto?> UpdateAsync(
        string conversationId, string scheduleId, PatchChatScheduleRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string conversationId, string scheduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delivers every active row due at <paramref name="nowUtc"/> — marks
    /// <c>LastDeliveredAt</c>, recomputes cron next-fires, deactivates
    /// one-shots, and enqueues a normal chat run per row (FIFO via
    /// <c>ChatRunQueue</c>). Returns the delivered count.
    /// </summary>
    Task<int> DeliverDueAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
