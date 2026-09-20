using Taskboard.Application.Contracts.Specs;

namespace Taskboard.Server.Services;

/// <summary>
/// Hourly living-spec drift scan (SPEC-20260920-harness-maintenance-jobs RF-003):
/// builds the report once an hour into <see cref="SpecDriftReportCache"/> and
/// logs newly drifted spec ids. Detection stays advisory — transitions are
/// applied manually via the /specs UI or status endpoint.
/// </summary>
public sealed class SpecDriftScanService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly ISpecDriftDetector _detector;
    private readonly SpecDriftReportCache _cache;
    private readonly ILogger<SpecDriftScanService> _logger;

    public SpecDriftScanService(
        ISpecDriftDetector detector,
        SpecDriftReportCache cache,
        ILogger<SpecDriftScanService> logger)
    {
        _detector = detector;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Spec drift scan started (interval {Interval}).", Interval);

        await SafeRunAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await SafeRunAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task SafeRunAsync(CancellationToken stoppingToken)
    {
        try
        {
            var previous = _cache.Last;
            var report = await _detector.BuildReportAsync(stoppingToken).ConfigureAwait(false);
            _cache.Update(report);

            var previousIds = previous is null
                ? []
                : previous.DriftItems.Select(i => i.SpecId).ToHashSet(StringComparer.Ordinal);
            var newItems = report.DriftItems.Where(i => !previousIds.Contains(i.SpecId)).ToList();
            if (newItems.Count > 0)
            {
                _logger.LogWarning(
                    "Spec drift scan: {NewCount} new drifted spec(s) of {Total} — {Ids}",
                    newItems.Count, report.StaleSpecsCount,
                    string.Join(", ", newItems.Select(i => $"{i.SpecId} → {i.SuggestedStatus}")));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Spec drift scan tick failed.");
        }
    }
}
