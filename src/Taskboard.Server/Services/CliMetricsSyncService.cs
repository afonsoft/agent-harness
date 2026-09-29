using Taskboard.Application.Contracts.CliMetrics;

namespace Taskboard.Server.Services;

/// <summary>
/// Periodic CLI metrics ingestion (<c>Taskboard:CliMetrics</c>) as managed job
/// <c>cli-metrics-sync</c> (SPEC-20260929-jobs-dashboard). The coordinator's
/// single-flight gate plus the registry's run gate guarantee no overlapping
/// syncs; a persisted JobSchedule override wins over the configured interval.
/// SPEC-20260919-cli-metrics RF-006.
/// </summary>
public sealed class CliMetricsSyncService : ManagedJobService
{
    public const string JobKey = "cli-metrics-sync";

    private readonly CliMetricsSyncCoordinator _coordinator;

    public CliMetricsSyncService(
        CliMetricsSyncCoordinator coordinator,
        JobRegistry registry,
        ILogger<CliMetricsSyncService> logger)
        : base(registry, JobKey, logger)
    {
        _coordinator = coordinator;
    }

    protected override async Task<string?> RunJobAsync(CancellationToken cancellationToken)
    {
        var result = await _coordinator.TrySyncAsync(cancellationToken).ConfigureAwait(false);
        return result.InFlight
            ? "sync already in flight"
            : $"{result.SourcesSynced} sources, {result.SessionsIngested} sessions ingested";
    }
}
