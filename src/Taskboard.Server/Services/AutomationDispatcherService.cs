using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Taskboard.Agents;
using Taskboard.Application.Automation;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Domain.Entities.Automation;
using Taskboard.Repositories;
using Taskboard.ValueObjects;

namespace Taskboard.Server.Services;

/// <summary>
/// SPEC-20261021-agent-automations RF-003/RF-004: fires due
/// <see cref="AgentAutomation"/> rows on a jittered 30s tick plus a boot
/// pass for missed fires (delivered once inside
/// <see cref="AgentAutomation.MissedRunGraceMinutes"/>, marked
/// <c>missed_window</c> beyond it). Also closes <c>running</c> run rows by
/// mapping their delegation task's terminal status. Single-writer — this
/// service is the only scheduled fire path.
/// </summary>
public sealed class AutomationDispatcherService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AutomationDispatcherService> logger,
    TimeProvider? clock = null) : BackgroundService
{
    private const int TickMs = 30_000;
    private const int JitterMs = 5_000;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled())
        {
            logger.LogInformation("automation dispatcher disabled (Taskboard:Automations:Enabled=false)");
            return;
        }

        await TickSafeAsync(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TickMs + RandomNumberGenerator.GetInt32(0, JitterMs);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(delay), _clock, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await TickSafeAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private bool Enabled() =>
        !string.Equals(
            configuration[AutomationService.EnabledKey], "false",
            StringComparison.OrdinalIgnoreCase);

    private async Task TickSafeAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var automations = sp.GetRequiredService<IRepository<AgentAutomation>>();
            var runs = sp.GetRequiredService<IRepository<AgentAutomationRun>>();
            var runner = sp.GetRequiredService<IAutomationRunner>();
            var taskRepo = sp.GetRequiredService<IDelegationTaskRepository>();

            var now = _clock.GetUtcNow().UtcDateTime;

            // Pass 1 — close runs whose delegation task reached a terminal state.
            var runningRuns = await runs.Query
                .Where(r => r.Outcome == AutomationRunOutcomes.Running && r.TaskId != null)
                .ToListAsync(stoppingToken).ConfigureAwait(false);
            foreach (var run in runningRuns)
            {
                var task = await taskRepo.GetAsync(run.TaskId!, stoppingToken).ConfigureAwait(false);
                if (task is null)
                {
                    continue;
                }
                if (task.Status == Taskboard.Delegation.DelegationTaskStatus.Done)
                {
                    run.Finish(succeeded: true, now, exitCode: 0);
                    await runs.UpdateAsync(run, stoppingToken).ConfigureAwait(false);
                }
                else if (task.Status is Taskboard.Delegation.DelegationTaskStatus.Failed
                    or Taskboard.Delegation.DelegationTaskStatus.Cancelled
                    or Taskboard.Delegation.DelegationTaskStatus.Stale)
                {
                    run.Finish(false, now, skipReason: $"task {task.Status}");
                    await runs.UpdateAsync(run, stoppingToken).ConfigureAwait(false);
                }
            }
            if (runningRuns.Count > 0)
            {
                await runs.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
            }

            // Pass 2 — fire due automations (missed-window guard inside).
            var due = await automations.Query
                .Where(a => a.Enabled && a.NextFireAtUtc <= now)
                .OrderBy(a => a.NextFireAtUtc)
                .Take(50)
                .ToListAsync(stoppingToken).ConfigureAwait(false);

            foreach (var automation in due)
            {
                // RF-004 missed window: overdue beyond grace → skipped row +
                // reschedule instead of a stale fire.
                var overdue = now - automation.NextFireAtUtc;
                if (overdue > TimeSpan.FromMinutes(automation.MissedRunGraceMinutes))
                {
                    var skipped = AgentAutomationRun.Skipped(
                        AgentAutomationRunId.NewGuid(), automation.Id,
                        automation.NextFireAtUtc,
                        $"missed_window: overdue {overdue.TotalMinutes:F0}m > grace {automation.MissedRunGraceMinutes}m");
                    await runs.AddAsync(skipped, stoppingToken).ConfigureAwait(false);

                    // RF-008: skipped fires surface on the feed too.
                    var feed = sp.GetService<IAgentFeedService>();
                    if (feed is not null)
                    {
                        try
                        {
                            await feed.ProduceAsync(
                                new FeedItemRequest(
                                    AgentFeedKind.AutomationRun,
                                    $"automation '{automation.Name}' skipped",
                                    skipped.SkipReason,
                                    $"/automations/{automation.Id.Value}",
                                    null,
                                    automation.AgentCli,
                                    null,
                                    NeedsAttention: true,
                                    automation.CreatedBy ?? "local",
                                    skipped.FiredAtUtc),
                                stoppingToken).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            logger.LogWarning(ex, "automation feed emit failed for {Id}", automation.Id.Value);
                        }
                    }

                    var next = RecomputeNext(automation, now);
                    if (next is { } nextFire)
                    {
                        automation.Reschedule(nextFire);
                    }
                    else
                    {
                        automation.SetEnabled(false);
                    }
                    await automations.UpdateAsync(automation, stoppingToken).ConfigureAwait(false);
                    await runs.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
                    continue;
                }

                await runner.FireAsync(automation.Id.Value, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutdown
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "automation dispatcher tick failed");
        }
    }

    private static DateTime? RecomputeNext(AgentAutomation automation, DateTime fromUtc)
    {
        if (automation.ScheduleKind != AutomationScheduleKinds.Cron
            || string.IsNullOrWhiteSpace(automation.CronExpression))
        {
            return null;
        }

        var tz = automation.TimeZoneId is { } id
            ? TimeZoneInfo.FindSystemTimeZoneById(id)
            : TimeZoneInfo.Utc;
        return Taskboard.Application.Contracts.Chat.ChatCronSchedule
            .Parse(automation.CronExpression).NextAfter(fromUtc, tz);
    }
}
