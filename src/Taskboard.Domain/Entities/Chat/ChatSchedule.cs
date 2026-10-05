using Taskboard;
using Taskboard.ValueObjects;

namespace Taskboard.Domain.Entities.Chat;

/// <summary>Kinds of scheduled follow-ups (SPEC-20261005-chat-jobs-schedule-search RF-004).</summary>
public static class ChatScheduleKinds
{
    /// <summary>Recurring 5-field cron expression (UTC unless a tz is given).</summary>
    public const string Cron = "cron";

    /// <summary>One-shot at an explicit UTC instant.</summary>
    public const string At = "at";

    /// <summary>One-shot N seconds from creation.</summary>
    public const string AfterSeconds = "after_seconds";

    public static bool IsValid(string kind) => kind is Cron or At or AfterSeconds;
}

/// <summary>
/// A conversation-bound scheduled follow-up (RF-004): at <see cref="NextFireAtUtc"/>
/// the dispatcher enqueues a normal chat run whose user message carries the
/// prompt (<c>Kind="schedule"</c>). One-shot kinds deactivate on delivery;
/// cron recomputes. Rows are durable — a server restart delivers missed fires
/// once at boot (guarded by <see cref="LastDeliveredAt"/>).
/// </summary>
public sealed class ChatSchedule : Entity<ChatScheduleId>
{
    public const int MaxTitleLength = 120;
    public const int MaxPromptLength = 4000;
    public const int MaxCronLength = 128;
    public const int MaxTimeZoneLength = 64;

    public ChatConversationId ConversationId { get; private set; } = default!;
    public string Kind { get; private set; } = default!;
    public string? CronExpression { get; private set; }
    /// <summary>IANA tz for cron evaluation (e.g. "America/Sao_Paulo"); null = UTC.</summary>
    public string? TimeZoneId { get; private set; }
    public DateTime NextFireAtUtc { get; private set; }
    public string Title { get; private set; } = default!;
    public string Prompt { get; private set; } = default!;
    public bool Active { get; private set; }
    public DateTime? LastDeliveredAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private ChatSchedule()
    {
    }

    private ChatSchedule(
        ChatScheduleId id, ChatConversationId conversationId, string kind,
        string? cronExpression, string? timeZoneId, DateTime nextFireAtUtc,
        string title, string prompt, DateTime createdAt)
        : base(id)
    {
        if (!ChatScheduleKinds.IsValid(kind))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, $"Unknown schedule kind '{kind}'.");
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Schedule prompt cannot be empty.");
        }

        if (prompt.Length > MaxPromptLength)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Schedule prompt exceeds {MaxPromptLength} chars.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            title = prompt.Length <= MaxTitleLength ? prompt : prompt[..MaxTitleLength];
        }

        if (title.Length > MaxTitleLength)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Schedule title exceeds {MaxTitleLength} chars.");
        }

        if (kind == ChatScheduleKinds.Cron && string.IsNullOrWhiteSpace(cronExpression))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "Cron schedules require an expression.");
        }

        ConversationId = conversationId;
        Kind = kind;
        CronExpression = cronExpression;
        TimeZoneId = string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId.Trim();
        NextFireAtUtc = nextFireAtUtc;
        Title = title.Trim();
        Prompt = prompt.Trim();
        Active = true;
        CreatedAt = createdAt;
    }

    public static ChatSchedule Create(
        ChatScheduleId id, ChatConversationId conversationId, string kind,
        string? cronExpression, string? timeZoneId, DateTime nextFireAtUtc,
        string title, string prompt, DateTime? now = null) =>
        new(id, conversationId, kind, cronExpression, timeZoneId, nextFireAtUtc,
            title ?? string.Empty, prompt, now ?? DateTime.UtcNow);

    /// <summary>
    /// Delivery stamp — for one-shot kinds (<c>at</c>/<c>after_seconds</c>)
    /// the schedule deactivates; for cron the caller passes the recomputed
    /// <paramref name="nextFireAtUtc"/>.
    /// </summary>
    public void MarkDelivered(DateTime now, DateTime? nextFireAtUtc)
    {
        LastDeliveredAt = now;
        if (Kind == ChatScheduleKinds.Cron && nextFireAtUtc is { } next)
        {
            NextFireAtUtc = next;
        }
        else
        {
            Active = false;
        }
    }

    /// <summary>Snooze/cancel without deleting the row (RF-005 update supports cancel).</summary>
    public void SetActive(bool active, DateTime? nextFireAtUtc = null)
    {
        Active = active;
        if (nextFireAtUtc is { } next)
        {
            NextFireAtUtc = next;
        }
    }

    /// <summary>Edits the prompt/title — applies to the next fire.</summary>
    public void Update(string? title, string? prompt, DateTime? now = null)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            if (title.Length > MaxTitleLength)
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidValue,
                    $"Schedule title exceeds {MaxTitleLength} chars.");
            }

            Title = title.Trim();
        }

        if (!string.IsNullOrWhiteSpace(prompt))
        {
            if (prompt.Length > MaxPromptLength)
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidValue,
                    $"Schedule prompt exceeds {MaxPromptLength} chars.");
            }

            Prompt = prompt.Trim();
        }
    }
}
