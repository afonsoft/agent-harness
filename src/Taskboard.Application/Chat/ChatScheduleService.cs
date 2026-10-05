using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Repositories;
using Taskboard.Chat;
using Taskboard.Domain.Entities.Chat;
using Taskboard.ValueObjects;

namespace Taskboard.Application.Chat;

/// <summary>
/// Conversation-scoped scheduled follow-ups (SPEC-20261005-chat-jobs-schedule-search
/// RF-004/RF-005/RF-006). The hosted dispatcher calls
/// <see cref="DeliverDueAsync"/> on every tick (and once at boot for missed
/// fires); tools and endpoints use the CRUD surface. Delivery is a normal
/// chat run whose user message carries <c>Kind="schedule"</c>.
/// </summary>
public sealed class ChatScheduleService : IChatScheduleService
{
    public const string EnabledKey = "Taskboard:Chat:Schedule:Enabled";
    public const string MaxPerConversationKey = "Taskboard:Chat:Schedule:MaxPerConversation";
    private const int DefaultMaxPerConversation = 20;

    private readonly IRepository<ChatSchedule> _schedules;
    private readonly IRepository<ChatConversation> _conversations;
    private readonly ChatService _chat;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _clock;
    private readonly ILogger<ChatScheduleService> _logger;

    public ChatScheduleService(
        IRepository<ChatSchedule> schedules,
        IRepository<ChatConversation> conversations,
        ChatService chat,
        IConfiguration configuration,
        ILogger<ChatScheduleService> logger,
        TimeProvider? clock = null)
    {
        _schedules = schedules;
        _conversations = conversations;
        _chat = chat;
        _configuration = configuration;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<ChatScheduleDto>> ListAsync(
        string conversationId, CancellationToken cancellationToken = default)
    {
        var rows = await _schedules.Query
            .Where(s => s.ConversationId == ChatConversationId.From(conversationId))
            .OrderBy(s => s.NextFireAtUtc)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(ToDto).ToList();
    }

    public async Task<ChatScheduleDto> CreateAsync(
        string conversationId, CreateChatScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversations
            .GetAsync(ChatConversationId.From(conversationId), cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ChatValidationException($"Conversation '{conversationId}' not found.");

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new ChatValidationException("Schedule prompt cannot be empty.");
        }

        var max = ParseMaxPerConversation();
        var count = await _schedules.Query
            .CountAsync(s => s.ConversationId == conversation.Id && s.Active, cancellationToken)
            .ConfigureAwait(false);
        if (count >= max)
        {
            throw new ChatConflictException(
                $"Schedule cap reached for this conversation ({max}).");
        }

        var now = UtcNow;
        var kind = request.Kind?.Trim().ToLowerInvariant() ?? string.Empty;
        var nextFireAtUtc = ComputeNextFire(kind, request, now);
        var timeZoneId = ResolveTimeZoneId(request.TimeZoneId)?.Id;

        var schedule = ChatSchedule.Create(
            ChatScheduleId.NewGuid(), conversation.Id,
            new ChatScheduleSpec(
                kind, kind == ChatScheduleKinds.Cron ? request.Expr : null,
                timeZoneId, nextFireAtUtc, request.Title ?? string.Empty, request.Prompt),
            now);
        await _schedules.AddAsync(schedule, cancellationToken).ConfigureAwait(false);
        await _schedules.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(schedule);
    }

    public async Task<ChatScheduleDto?> UpdateAsync(
        string conversationId, string scheduleId, PatchChatScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        var schedule = await GetOwnedAsync(conversationId, scheduleId, cancellationToken)
            .ConfigureAwait(false);
        if (schedule is null)
        {
            return null;
        }

        schedule.Update(request.Title, request.Prompt, UtcNow);
        if (request.Active is { } active)
        {
            // Reactivating a one-shot re-arms it "now"; cron recomputes.
            DateTime? nextFire = null;
            if (active && schedule.Kind == ChatScheduleKinds.Cron)
            {
                nextFire = ChatCronSchedule.Parse(schedule.CronExpression ?? "")
                    .NextAfter(UtcNow, ResolveTimeZoneId(schedule.TimeZoneId));
            }
            else if (active && schedule.NextFireAtUtc <= UtcNow)
            {
                nextFire = UtcNow;
            }

            schedule.SetActive(active, nextFire);
        }

        await _schedules.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(schedule);
    }

    public async Task<bool> DeleteAsync(
        string conversationId, string scheduleId, CancellationToken cancellationToken = default)
    {
        var schedule = await GetOwnedAsync(conversationId, scheduleId, cancellationToken)
            .ConfigureAwait(false);
        if (schedule is null)
        {
            return false;
        }

        await _schedules.DeleteAsync(schedule, cancellationToken).ConfigureAwait(false);
        await _schedules.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<int> DeliverDueAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var due = await _schedules.Query
            .Where(s => s.Active && s.NextFireAtUtc <= nowUtc)
            .OrderBy(s => s.NextFireAtUtc)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (due.Count == 0)
        {
            return 0;
        }

        var delivered = 0;
        foreach (var schedule in due)
        {
            try
            {
                await _chat.EnqueueScheduledMessageAsync(
                    schedule.ConversationId.Value, schedule.Prompt, cancellationToken)
                    .ConfigureAwait(false);

                // cron recomputes past `nowUtc` — missed periods collapse into
                // one delivery per row (RF-006: missed fires deliver once).
                var next = schedule.Kind == ChatScheduleKinds.Cron
                    ? ChatCronSchedule.Parse(schedule.CronExpression ?? "")
                        .NextAfter(nowUtc, ResolveTimeZoneId(schedule.TimeZoneId))
                    : (DateTime?)null;
                schedule.MarkDelivered(nowUtc, next);
                delivered++;
            }
            catch (ChatValidationException ex)
            {
                // Conversation gone/archived/provider deleted — deactivate
                // instead of retrying the same row every tick.
                _logger.LogWarning(ex, "schedule {ScheduleId} undeliverable — deactivating", schedule.Id.Value);
                schedule.SetActive(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Transient failure — leave due so the next tick retries.
                _logger.LogWarning(ex, "schedule {ScheduleId} delivery failed — retry next tick", schedule.Id.Value);
            }
        }

        await _schedules.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        return delivered;
    }

    private async Task<ChatSchedule?> GetOwnedAsync(
        string conversationId, string scheduleId, CancellationToken cancellationToken)
    {
        var schedule = await _schedules
            .GetAsync(ChatScheduleId.From(scheduleId), cancellationToken)
            .ConfigureAwait(false);
        return schedule is not null
            && string.Equals(schedule.ConversationId.Value, conversationId, StringComparison.Ordinal)
                ? schedule
                : null;
    }

    private static DateTime ComputeNextFire(string kind, CreateChatScheduleRequest request, DateTime now)
    {
        switch (kind)
        {
            case ChatScheduleKinds.At:
                if (!DateTimeOffset.TryParse(request.Expr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at))
                {
                    throw new ChatValidationException("kind=at requires expr as an ISO-8601 instant.");
                }

                var utc = at.UtcDateTime;
                if (utc <= now)
                {
                    throw new ChatValidationException("kind=at requires a future instant.");
                }

                return utc;

            case ChatScheduleKinds.AfterSeconds:
                if (request.AfterSeconds is not > 0)
                {
                    throw new ChatValidationException("kind=after_seconds requires after_seconds > 0.");
                }

                return now.AddSeconds(request.AfterSeconds.Value);

            case ChatScheduleKinds.Cron:
                ChatCronSchedule cron;
                try
                {
                    cron = ChatCronSchedule.Parse(request.Expr ?? string.Empty);
                }
                catch (FormatException ex)
                {
                    throw new ChatValidationException($"Invalid cron expression: {ex.Message}");
                }

                return cron.NextAfter(now, ResolveTimeZoneId(request.TimeZoneId));

            default:
                throw new ChatValidationException(
                    "Schedule kind must be 'cron', 'at' or 'after_seconds'.");
        }
    }

    private static TimeZoneInfo? ResolveTimeZoneId(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            throw new ChatValidationException($"Unknown timezone '{timeZoneId}'.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new ChatValidationException($"Invalid timezone '{timeZoneId}'.");
        }
    }

    private int ParseMaxPerConversation()
    {
        var raw = _configuration[MaxPerConversationKey];
        return int.TryParse(raw, out var value) && value > 0 ? value : DefaultMaxPerConversation;
    }

    private static ChatScheduleDto ToDto(ChatSchedule schedule) => new(
        Id: schedule.Id.Value,
        ConversationId: schedule.ConversationId.Value,
        Kind: schedule.Kind,
        CronExpression: schedule.CronExpression,
        TimeZoneId: schedule.TimeZoneId,
        NextFireAtUtc: schedule.NextFireAtUtc,
        Title: schedule.Title,
        Prompt: schedule.Prompt,
        Active: schedule.Active,
        LastDeliveredAt: schedule.LastDeliveredAt,
        CreatedAt: schedule.CreatedAt);
}
