using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Server.Services;

/// <summary>
/// SPEC-20261020-provider-quota-usage RF-004: samples provider quotas every
/// <c>Taskboard:ProviderUsage:SamplingIntervalMinutes</c> (default 5min).
/// Tolerant — a failing collector logs and the tick retries next pass; the
/// job never dies.
/// </summary>
public sealed class ProviderUsageCollectJob : BackgroundService
{
    private readonly IProviderUsageService _usage;
    private readonly ILogger<ProviderUsageCollectJob> _logger;
    private readonly TimeSpan _interval;

    public ProviderUsageCollectJob(
        IProviderUsageService usage,
        IConfiguration configuration,
        ILogger<ProviderUsageCollectJob> logger)
    {
        _usage = usage;
        _logger = logger;
        _interval = configuration["Taskboard:ProviderUsage:SamplingIntervalMinutes"]
            is { Length: > 0 } raw && int.TryParse(raw, out var minutes)
                ? TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 60))
                : TimeSpan.FromMinutes(5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await TickAsync(stoppingToken).ConfigureAwait(false); // first badge ASAP.
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await TickAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>Tolerant tick — a failure ages samples, never kills the job.</summary>
    private async Task TickAsync(CancellationToken ct)
    {
        try
        {
            var count = await _usage.CollectOnceAsync("local", ct).ConfigureAwait(false);
            if (count > 0)
            {
                _logger.LogInformation("Sampled {Count} provider usage window(s).", count);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Provider usage collect tick failed.");
        }
    }
}
