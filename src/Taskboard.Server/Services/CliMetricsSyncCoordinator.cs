using Taskboard.Application.Contracts.CliMetrics;
using Taskboard.Dtos;

namespace Taskboard.Server.Services;

/// <summary>
/// Single-flight gate for CLI metrics syncs — shared by the periodic hosted
/// service and the manual endpoint so overlapping runs never start.
/// SPEC-20260919-cli-metrics RF-006.
/// </summary>
public sealed class CliMetricsSyncCoordinator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private int _inFlight;

    public CliMetricsSyncCoordinator(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Runs a sync if none is in flight; otherwise returns immediately with
    /// <see cref="CliMetricsSyncResultDto.InFlight"/> set.
    /// </summary>
    public async Task<CliMetricsSyncResultDto> TrySyncAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _inFlight, 1) == 1)
        {
            return new CliMetricsSyncResultDto("in-flight", InFlight: true, 0, 0, Error: null);
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<ICliMetricsService>();
            return await service.SyncAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _inFlight, 0);
        }
    }
}
