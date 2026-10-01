using Taskboard.Integrations.Agents;

namespace Taskboard.Server.Services;

/// <summary>
/// Managed job <c>cli-probe-refresh</c> (SPEC-20260929-jobs-dashboard RF-001):
/// kicks the first CLI probe refresh at startup without blocking the boot and
/// re-refreshes hourly so a long-running server never serves a stale
/// versions/models catalog (TTL-on-read only reacts to reads).
/// SPEC-20260928-agent-cli-probe-background.
/// </summary>
public sealed class CliProbeRefreshJobService : ManagedJobService
{
    public const string JobKey = "cli-probe-refresh";

    private readonly CliProbeSnapshotService _snapshot;

    public CliProbeRefreshJobService(
        CliProbeSnapshotService snapshot,
        JobRegistry registry,
        ILogger<CliProbeRefreshJobService> logger)
        : base(registry, JobKey, logger)
    {
        _snapshot = snapshot;
    }

    protected override async Task<string?> RunJobAsync(CancellationToken cancellationToken)
    {
        // RefreshAsync is single-flight (joins an in-flight refresh) and bounds
        // the probes with its own timeout, so the job token is not forwarded.
        await _snapshot.RefreshAsync().ConfigureAwait(false);
        // B-21: a wholesale refresh failure (timeout/exception) must surface
        // as a job failure instead of a misleading "ok" outcome.
        if (_snapshot.LastRefreshError is { } error)
        {
            throw new InvalidOperationException($"probe refresh failed: {error}");
        }

        return "probe snapshot refreshed";
    }
}
