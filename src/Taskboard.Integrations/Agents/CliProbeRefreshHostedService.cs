using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// SPEC-20260928-agent-cli-probe-background: kicks the first CLI probe
/// refresh at startup without blocking the boot — the snapshot is served
/// immediately and versions/models fill in when the refresh completes.
/// </summary>
public sealed class CliProbeRefreshHostedService(
    CliProbeSnapshotService snapshot,
    ILogger<CliProbeRefreshHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await snapshot.RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Startup CLI probe refresh failed.");
        }
    }
}
