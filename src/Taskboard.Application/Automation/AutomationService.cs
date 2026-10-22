using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Domain.Entities.Automation;
using Taskboard.Dtos;
using Taskboard.Repositories;
using Taskboard.ValueObjects;

namespace Taskboard.Application.Automation;

/// <summary>
/// SPEC-20261021-agent-automations RF-001/RF-006: CRUD + manual fire + run
/// history for scheduled agent prompts. Schedule resolution (presets → cron,
/// ISO instant → one-shot) happens at save time; <see cref="IAutomationRunner"/>
/// owns the fire path shared with the hosted dispatcher.
/// </summary>
public sealed class AutomationService : IAutomationService
{
    public const string EnabledKey = "Taskboard:Automations:Enabled";
    public const string MaxPerUserKey = "Taskboard:Automations:MaxPerUser";
    private const int DefaultMaxPerUser = 200;

    private static readonly IReadOnlyDictionary<string, string> Presets =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["hourly"] = "0 * * * *",
            ["daily"] = "0 9 * * *",
            ["weekdays"] = "0 9 * * 1-5",
            ["weekly"] = "0 9 * * 1",
        };

    private static readonly string[] TargetKinds = ["repo", "worktree", "project"];

    private readonly IRepository<AgentAutomation> _automations;
    private readonly IRepository<AgentAutomationRun> _runs;
    private readonly IAutomationRunner _runner;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _clock;

    public AutomationService(
        IRepository<AgentAutomation> automations,
        IRepository<AgentAutomationRun> runs,
        IAutomationRunner runner,
        IConfiguration configuration,
        TimeProvider? clock = null)
    {
        _automations = automations;
        _runs = runs;
        _runner = runner;
        _configuration = configuration;
        _clock = clock ?? TimeProvider.System;
    }

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

    public async Task<AgentAutomationDto> CreateAsync(CreateAutomationRequest request, CancellationToken ct = default)
    {
        if (string.Equals(_configuration[EnabledKey], "false", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Automations are disabled.");
        }

        ValidateTarget(request.Target);
        var (kind, cron, next) = ResolveSchedule(request.Schedule, request.TimeZoneId);
        var tz = NormalizeTimeZone(request.TimeZoneId);

        var createdBy = "local";
        var max = int.TryParse(_configuration[MaxPerUserKey], out var configured) && configured > 0
            ? configured
            : DefaultMaxPerUser;
        var count = await _automations.Query
            .Where(a => a.CreatedBy == createdBy)
            .CountAsync(ct).ConfigureAwait(false);
        if (count >= max)
        {
            throw new DomainException(
                "automation_limit",
                $"Automation limit reached ({max}).");
        }

        var automation = AgentAutomation.Create(
            AgentAutomationId.NewGuid(),
            new AgentAutomationSpec(
                request.Name,
                request.Prompt,
                request.AgentCli,
                request.Target,
                kind,
                cron,
                tz,
                next,
                request.Precheck,
                request.ExtraAgentArgs,
                request.MissedRunGraceMinutes ?? 60,
                request.ReuseSession,
                request.Host,
                createdBy));

        await _automations.AddAsync(automation, ct).ConfigureAwait(false);
        await _automations.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(automation);
    }

    public async Task<AgentAutomationDto?> GetAsync(string id, CancellationToken ct = default)
    {
        var entity = await _automations.GetAsync(AgentAutomationId.From(id), ct).ConfigureAwait(false);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<IReadOnlyList<AgentAutomationDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _automations.Query
            .OrderBy(a => a.Name)
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(ToDto).ToList();
    }

    public async Task<AgentAutomationDto?> UpdateAsync(string id, UpdateAutomationRequest request, CancellationToken ct = default)
    {
        var entity = await _automations.GetAsync(AgentAutomationId.From(id), ct).ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        if (request.Target is not null)
        {
            ValidateTarget(request.Target);
        }

        entity.Update(
            request.Name,
            request.Prompt,
            request.Target,
            request.Precheck,
            request.ExtraAgentArgs,
            request.MissedRunGraceMinutes,
            request.ReuseSession);

        if (request.Schedule is not null || request.TimeZoneId is not null)
        {
            var (kind, cron, next) = ResolveSchedule(
                request.Schedule ?? entity.CronExpression ?? "at",
                request.TimeZoneId ?? entity.TimeZoneId);
            entity.SetSchedule(kind, cron, request.TimeZoneId ?? entity.TimeZoneId);
            entity.Reschedule(next);
        }

        if (request.Enabled is { } enabled)
        {
            entity.SetEnabled(enabled);
        }

        await _automations.UpdateAsync(entity, ct).ConfigureAwait(false);
        await _automations.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var entity = await _automations.GetAsync(AgentAutomationId.From(id), ct).ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }

        await _automations.DeleteAsync(entity, ct).ConfigureAwait(false);
        await _automations.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<AgentAutomationRunDto?> RunNowAsync(string id, CancellationToken ct = default)
    {
        var entity = await _automations.GetAsync(AgentAutomationId.From(id), ct).ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        return await _runner.FireAsync(id, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentAutomationRunDto>> ListRunsAsync(
        string id, int take = 50, CancellationToken ct = default)
    {
        var rows = await _runs.Query
            .Where(r => r.AutomationId == AgentAutomationId.From(id))
            .OrderByDescending(r => r.FiredAtUtc)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(ToRunDto).ToList();
    }

    /// <summary>
    /// Resolves <paramref name="schedule"/> into (kind, cronExpr, nextFireUtc):
    /// presets → cron, ISO-8601 instant → one-shot <c>at</c>, otherwise a
    /// 5-field cron validated by <see cref="ChatCronSchedule"/>.
    /// </summary>
    internal static (string Kind, string? Cron, DateTime Next) ResolveSchedule(
        string schedule, string? timeZoneId, DateTime? now = null)
    {
        var fromUtc = now ?? DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(schedule))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Schedule is required.");
        }

        var text = schedule.Trim();
        var expr = Presets.TryGetValue(text, out var preset) ? preset : text;

        // ISO instant → one-shot.
        if (DateTimeOffset.TryParse(
                text, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var instant)
            && text.Contains('T'))
        {
            if (instant.UtcDateTime <= fromUtc)
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidValue,
                    "One-shot schedule must be in the future.");
            }
            return (AutomationScheduleKinds.At, null, instant.UtcDateTime);
        }

        var tz = ResolveTimeZone(timeZoneId);
        ChatCronSchedule parsed;
        try
        {
            parsed = ChatCronSchedule.Parse(expr);
        }
        catch (FormatException ex)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Invalid cron schedule: {ex.Message}");
        }

        var next = parsed.NextAfter(fromUtc, tz);
        return (AutomationScheduleKinds.Cron, expr, next);
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Utc;
        }
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Unknown time zone '{timeZoneId}'.");
        }
    }

    private static string? NormalizeTimeZone(string? timeZoneId)
    {
        _ = ResolveTimeZone(timeZoneId); // validates
        return string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId.Trim();
    }

    private static void ValidateTarget(string target)
    {
        var ok = !string.IsNullOrWhiteSpace(target)
            && TargetKinds.Any(k => target.StartsWith($"{k}:", StringComparison.Ordinal));
        if (!ok)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                "Target must start with repo:, worktree: or project:.");
        }
    }

    public static AgentAutomationDto ToDto(AgentAutomation a) => new(
        a.Id.Value, a.Name, a.AgentCli, a.Target, a.ScheduleKind,
        a.CronExpression, a.TimeZoneId, a.NextFireAtUtc, a.Enabled,
        a.MissedRunGraceMinutes, a.ReuseSession, a.ExtraAgentArgs,
        a.Precheck, a.Host ?? "local", a.CreatedBy, a.CreatedAt,
        a.LastFiredAtUtc, a.Version);

    public static AgentAutomationRunDto ToRunDto(AgentAutomationRun r) => new(
        r.Id.Value, r.AutomationId.Value, r.FiredAtUtc, r.Outcome,
        r.ExitCode, r.TaskId, r.SessionId, r.SkipReason, r.DurationMs,
        r.FinishedAtUtc);
}
