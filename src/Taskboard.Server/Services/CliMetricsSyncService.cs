using Taskboard.Application.Contracts.CliMetrics;

namespace Taskboard.Server.Services;

/// <summary>
/// Periodic CLI metrics ingestion (<c>Taskboard:CliMetrics</c>). Sequential
/// timer ticks plus the coordinator's single-flight gate guarantee no
/// overlapping syncs; a disabled flag stops all background ingestion.
/// SPEC-20260919-cli-metrics RF-006.
/// </summary>
public sealed class CliMetricsSyncService : BackgroundService
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(1);

    private readonly CliMetricsSyncCoordinator _coordinator;
    private readonly CliMetricsOptions _options;
    private readonly ILogger<CliMetricsSyncService> _logger;

    public CliMetricsSyncService(
        CliMetricsSyncCoordinator coordinator,
        CliMetricsOptions options,
        ILogger<CliMetricsSyncService> logger)
    {
        _coordinator = coordinator;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("CLI metrics sync disabled (Taskboard:CliMetrics:Enabled=false).");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.SyncIntervalMinutes));
        _logger.LogInformation("CLI metrics sync started (interval {Interval}).", interval);

        // Initial pass so fresh installs get data without waiting a full interval.
        await SafeSyncAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await SafeSyncAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task SafeSyncAsync(CancellationToken stoppingToken)
    {
        try
        {
            var result = await _coordinator.TrySyncAsync(stoppingToken).ConfigureAwait(false);
            if (!result.InFlight)
            {
                _logger.LogDebug(
                    "CLI metrics sync: {Sources} sources, {Sessions} sessions ingested",
                    result.SourcesSynced, result.SessionsIngested);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CLI metrics sync tick failed.");
        }
    }
}
