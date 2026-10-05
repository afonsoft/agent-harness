using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Chat;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// SPEC-20261005-chat-jobs-schedule-search RF-005: schedule tools — the
/// service is scoped (EF), so the singleton tool resolves it per call.
/// </summary>
public sealed class ScheduleCreateTool(IServiceScopeFactory scopeFactory, IConfiguration configuration) : IChatTool
{
    public string Name => "schedule_create";
    public string Description =>
        "Schedule a future message in this conversation. kind=cron needs a "
        + "5-field expr (+optional timezone), kind=at needs an ISO instant expr, "
        + "kind=after_seconds needs after_seconds.";
    public string ParametersJson => """
        {"type":"object","properties":{"kind":{"type":"string","enum":["cron","at","after_seconds"]},"expr":{"type":"string","description":"cron expression (cron) or ISO-8601 instant (at)"},"timezone":{"type":"string","description":"IANA timezone for cron (default UTC)"},"after_seconds":{"type":"integer"},"title":{"type":"string","description":"Short label, <=120 chars"},"prompt":{"type":"string","description":"Message delivered when the schedule fires"}},"required":["kind","prompt"]}
        """;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        if (context.ConversationId is not { } conversationId)
        {
            return Refused("schedules unavailable without a conversation");
        }

        if (!ChatFeatureFlags.IsEnabled(configuration, ChatFeatureFlags.ScheduleEnabledKey))
        {
            return Refused("chat schedules are disabled");
        }

        var kind = StringArg(arguments, "kind");
        if (kind is null)
        {
            return Refused("kind is required");
        }

        var prompt = StringArg(arguments, "prompt") ?? string.Empty;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var schedules = scope.ServiceProvider.GetRequiredService<IChatScheduleService>();
            var dto = await schedules.CreateAsync(conversationId, new CreateChatScheduleRequest(
                Kind: kind,
                Expr: StringArg(arguments, "expr"),
                TimeZoneId: StringArg(arguments, "timezone"),
                AfterSeconds: IntArg(arguments, "after_seconds"),
                Title: StringArg(arguments, "title"),
                Prompt: prompt), cancellationToken).ConfigureAwait(false);
            return new ChatToolResult(JsonSerializer.Serialize(new
            {
                scheduleId = dto.Id,
                dto.Kind,
                nextFireAtUtc = dto.NextFireAtUtc,
            }));
        }
        catch (ChatValidationException ex)
        {
            return Refused(ex.Message);
        }
        catch (ChatConflictException ex)
        {
            return Refused(ex.Message);
        }
    }

    private static string? StringArg(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int? IntArg(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var v) && v.TryGetInt32(out var iv) ? iv : null;

    private static ChatToolResult Refused(string reason) =>
        new(JsonSerializer.Serialize(new { error = reason }), Refused: true, reason);
}

/// <summary>SPEC-20261005 RF-005: list the conversation's schedules.</summary>
public sealed class ScheduleListTool(IServiceScopeFactory scopeFactory, IConfiguration configuration) : IChatTool
{
    private static ChatToolResult Refused(string reason) =>
        new(JsonSerializer.Serialize(new { error = reason }), Refused: true, reason);

    public string Name => "schedule_list";
    public string Description => "List this conversation's scheduled follow-ups (active and past).";
    public string ParametersJson => """{"type":"object","properties":{}}""";

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        if (context.ConversationId is not { } conversationId)
        {
            return new ChatToolResult(
                JsonSerializer.Serialize(new { error = "schedules unavailable without a conversation" }),
                Refused: true, "no conversation");
        }

        if (!ChatFeatureFlags.IsEnabled(configuration, ChatFeatureFlags.ScheduleEnabledKey))
        {
            return Refused("chat schedules are disabled");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var schedules = scope.ServiceProvider.GetRequiredService<IChatScheduleService>();
        var rows = await schedules.ListAsync(conversationId, cancellationToken).ConfigureAwait(false);
        return new ChatToolResult(JsonSerializer.Serialize(new
        {
            schedules = rows.Select(s => new
            {
                scheduleId = s.Id,
                s.Kind,
                s.CronExpression,
                s.TimeZoneId,
                s.NextFireAtUtc,
                s.Title,
                s.Active,
                s.LastDeliveredAt,
            }).ToList(),
        }));
    }
}

/// <summary>SPEC-20261005 RF-005: edit prompt/title or activate-deactivate a schedule.</summary>
public sealed class ScheduleUpdateTool(IServiceScopeFactory scopeFactory, IConfiguration configuration) : IChatTool
{
    public string Name => "schedule_update";
    public string Description =>
        "Update a schedule: edit title/prompt, or pass active=false to cancel / true to re-enable.";
    public string ParametersJson =>
        """{"type":"object","properties":{"schedule_id":{"type":"string"},"active":{"type":"boolean"},"title":{"type":"string"},"prompt":{"type":"string"}},"required":["schedule_id"]}""";

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        if (context.ConversationId is not { } conversationId)
        {
            return Refused("schedules unavailable without a conversation");
        }

        if (!ChatFeatureFlags.IsEnabled(configuration, ChatFeatureFlags.ScheduleEnabledKey))
        {
            return Refused("chat schedules are disabled");
        }

        if (!arguments.TryGetProperty("schedule_id", out var sid) || sid.ValueKind != JsonValueKind.String)
        {
            return Refused("schedule_id is required");
        }

        bool? active = arguments.TryGetProperty("active", out var a)
            && a.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? a.GetBoolean()
                : null;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var schedules = scope.ServiceProvider.GetRequiredService<IChatScheduleService>();
            var dto = await schedules.UpdateAsync(conversationId, sid.GetString()!,
                new PatchChatScheduleRequest(
                    Active: active,
                    Title: StringArg(arguments, "title"),
                    Prompt: StringArg(arguments, "prompt")),
                cancellationToken).ConfigureAwait(false);
            if (dto is null)
            {
                return Refused($"schedule '{sid.GetString()}' not found");
            }

            return new ChatToolResult(JsonSerializer.Serialize(new
            {
                scheduleId = dto.Id,
                dto.Active,
                nextFireAtUtc = dto.NextFireAtUtc,
            }));
        }
        catch (ChatValidationException ex)
        {
            return Refused(ex.Message);
        }
    }

    private static string? StringArg(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static ChatToolResult Refused(string reason) =>
        new(JsonSerializer.Serialize(new { error = reason }), Refused: true, reason);
}

/// <summary>SPEC-20261005 RF-005: delete a schedule row entirely.</summary>
public sealed class ScheduleDeleteTool(IServiceScopeFactory scopeFactory, IConfiguration configuration) : IChatTool
{
    public string Name => "schedule_delete";
    public string Description => "Delete a scheduled follow-up permanently.";
    public string ParametersJson =>
        """{"type":"object","properties":{"schedule_id":{"type":"string"}},"required":["schedule_id"]}""";

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        if (context.ConversationId is not { } conversationId)
        {
            return Refused("schedules unavailable without a conversation");
        }

        if (!ChatFeatureFlags.IsEnabled(configuration, ChatFeatureFlags.ScheduleEnabledKey))
        {
            return Refused("chat schedules are disabled");
        }

        if (!arguments.TryGetProperty("schedule_id", out var sid) || sid.ValueKind != JsonValueKind.String)
        {
            return Refused("schedule_id is required");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var schedules = scope.ServiceProvider.GetRequiredService<IChatScheduleService>();
        var deleted = await schedules.DeleteAsync(conversationId, sid.GetString()!, cancellationToken)
            .ConfigureAwait(false);
        return deleted
            ? new ChatToolResult(JsonSerializer.Serialize(new { deleted = true }))
            : Refused($"schedule '{sid.GetString()}' not found");
    }

    private static ChatToolResult Refused(string reason) =>
        new(JsonSerializer.Serialize(new { error = reason }), Refused: true, reason);
}
