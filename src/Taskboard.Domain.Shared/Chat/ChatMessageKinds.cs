namespace Taskboard.Chat;

/// <summary>
/// SPEC-20261005-chat-context-management RF-004: <c>ChatMessage.Kind</c>
/// discriminator — "normal" rows vs persisted compaction summaries
/// ("summary" rows carry SupersedesUntilMessageId and are re-emitted on the
/// wire in place of the superseded prefix).
/// </summary>
public static class ChatMessageKinds
{
    public const string Normal = "normal";
    public const string Summary = "summary";

    /// <summary>
    /// SPEC-20261005-chat-fork-steering RF-008: a user message that entered
    /// mid-turn via steering — rendered with a steer marker in the transcript.
    /// </summary>
    public const string Steer = "steer";

    /// <summary>
    /// SPEC-20261005-chat-jobs-schedule-search RF-006: a user message a
    /// ChatSchedule fired — badge in the transcript, plain user role on the wire.
    /// </summary>
    public const string Schedule = "schedule";
}
