using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Chat;

namespace Taskboard.Server.Services;

/// <summary>
/// Fires due <c>ChatSchedule</c> rows (SPEC-20261005-chat-jobs-schedule-search
/// RF-004/RF-006, RNF-002): a 30s jittered tick plus a boot pass that
/// delivers missed fires once (rows durable; delivery enqueues a normal run
/// through <c>ChatRunQueue</c>, so ordering backpressure is the run
/// dispatcher's). Single-writer — this service is the only deliverer.
/// </summary>
public sealed class ChatScheduleDispatcherService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ChatScheduleDispatcherService> logger,
    TimeProvider? clock = null) : BackgroundService
{
    private const int TickMs = 30_000;
    private const int JitterMs = 5_000;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled())
        {
            logger.LogInformation("chat schedule dispatcher disabled (Taskboard:Chat:Schedule:Enabled=false)");
            return;
        }

        await DeliverDueSafeAsync(stoppingToken).ConfigureAwait(false);

        var random = new Random();
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TickMs + random.Next(0, JitterMs);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(delay), _clock, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await DeliverDueSafeAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private bool Enabled() =>
        !string.Equals(
            configuration[ChatScheduleService.EnabledKey], "false",
            StringComparison.OrdinalIgnoreCase);

    private async Task DeliverDueSafeAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var schedules = scope.ServiceProvider.GetRequiredService<IChatScheduleService>();
            var delivered = await schedules.DeliverDueAsync(
                _clock.GetUtcNow().UtcDateTime, stoppingToken).ConfigureAwait(false);
            if (delivered > 0)
            {
                logger.LogInformation("chat schedule dispatcher delivered {Count} schedule(s)", delivered);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown mid-tick — rows stay due; the next boot pass delivers them.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "chat schedule dispatcher tick failed");
        }
    }
}
