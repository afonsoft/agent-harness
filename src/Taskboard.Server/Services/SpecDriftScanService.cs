using Taskboard.Application.Contracts.Specs;
using Taskboard.Dtos;

namespace Taskboard.Server.Services;

/// <summary>
/// Living-spec drift scan (SPEC-20260920-harness-maintenance-jobs RF-003):
/// builds the report into <see cref="SpecDriftReportCache"/> and logs newly
/// drifted spec ids. Managed job <c>spec-drift-scan</c>
/// (SPEC-20260929-jobs-dashboard), default hourly. Detection stays advisory —
/// transitions are applied manually via the /specs UI or status endpoint.
/// </summary>
public sealed class SpecDriftScanService : ManagedJobService
{
    public const string JobKey = "spec-drift-scan";

    private readonly ISpecDriftDetector _detector;
    private readonly SpecDriftReportCache _cache;
    // SPEC-20261004-redis-hybrid-cache RF-005: the "new drifts since last scan"
    // diff only makes sense inside this process — keep it local, push reports
    // to the shared HybridCache.
    private SpecDriftReportDto? _previous;

    public SpecDriftScanService(
        ISpecDriftDetector detector,
        SpecDriftReportCache cache,
        JobRegistry registry,
        ILogger<SpecDriftScanService> logger)
        : base(registry, JobKey, logger)
    {
        _detector = detector;
        _cache = cache;
    }

    protected override async Task<string?> RunJobAsync(CancellationToken cancellationToken)
    {
        var previous = _previous;
        var report = await _detector.BuildReportAsync(null, cancellationToken).ConfigureAwait(false);
        _previous = report;
        await _cache.SetAsync(report, cancellationToken).ConfigureAwait(false);

        var previousIds = previous is null
            ? []
            : previous.DriftItems.Select(i => i.SpecId).ToHashSet(StringComparer.Ordinal);
        var newItems = report.DriftItems.Where(i => !previousIds.Contains(i.SpecId)).ToList();
        return newItems.Count > 0
            ? $"{newItems.Count} new drifted spec(s) of {report.StaleSpecsCount} — " +
                string.Join(", ", newItems.Select(i => $"{i.SpecId} → {i.SuggestedStatus}"))
            : $"{report.StaleSpecsCount} drifted spec(s)";
    }
}
