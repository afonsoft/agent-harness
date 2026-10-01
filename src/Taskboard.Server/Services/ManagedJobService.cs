using Taskboard.Application.Contracts.Jobs;

namespace Taskboard.Server.Services;

/// <summary>
/// Base class for managed background jobs (SPEC-20260929-jobs-dashboard):
/// replaces the ad-hoc PeriodicTimer loops. Effective schedule (persisted
/// override ?? definition default) is re-read every iteration, so
/// <c>PUT /api/jobs/{key}</c> takes effect without restart. A channel wake
/// handles manual runs and schedule changes; <see cref="JobRegistry.TryBeginRun"/>
/// keeps ticks and manual runs single-flight.
/// </summary>
public abstract class ManagedJobService : BackgroundService
{
    private readonly JobRegistry _registry;
    private readonly string _key;
    private readonly ILogger _logger;

    protected ManagedJobService(JobRegistry registry, string key, ILogger logger)
    {
        _registry = registry;
        _key = key;
        _logger = logger;
    }

    /// <summary>One job iteration. Return a short result message for the job log.</summary>
    protected abstract Task<string?> RunJobAsync(CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var signals = _registry.GetSignalReader(_key);
        var schedule = await _registry.GetEffectiveAsync(_key, stoppingToken).ConfigureAwait(false);
        if (schedule.Enabled)
        {
            await RunOnceSafeAsync(stoppingToken).ConfigureAwait(false);
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                schedule = await _registry.GetEffectiveAsync(_key, stoppingToken).ConfigureAwait(false);
                var wait = schedule.Enabled && !schedule.RunOnce
                    ? schedule.Interval
                    : Timeout.InfiniteTimeSpan;

                var delayTask = Task.Delay(wait, stoppingToken);
                var signalTask = signals.ReadAsync(stoppingToken).AsTask();
                var winner = await Task.WhenAny(delayTask, signalTask).ConfigureAwait(false);
                // SPEC-20260929-managed-job-shutdown-extra-run RF-001: a
                // delayTask cancelled by StopAsync also wins WhenAny — without
                // the IsCompletedSuccessfully check it would fire a spurious
                // run on every shutdown (even for disabled jobs).
                if (winner == delayTask && delayTask.IsCompletedSuccessfully)
                {
                    await RunOnceSafeAsync(stoppingToken).ConfigureAwait(false);
                    continue;
                }

                var runRequested = await signalTask.ConfigureAwait(false) == JobSignal.RunRequested;
                while (signals.TryRead(out var extra))
                {
                    runRequested |= extra == JobSignal.RunRequested;
                }

                if (runRequested)
                {
                    await RunOnceSafeAsync(stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Cancellation is the expected shutdown path — nothing to clean up.
        }
    }

    private async Task RunOnceSafeAsync(CancellationToken stoppingToken)
    {
        if (!_registry.TryBeginRun(_key))
        {
            return;
        }

        try
        {
            var message = await RunJobAsync(stoppingToken).ConfigureAwait(false);
            _registry.ReportFinished(_key, true, message);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _registry.ReportFinished(_key, true, "cancelled on shutdown");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Managed job '{Key}' failed.", _key);
            _registry.ReportFinished(_key, false, ex.Message);
        }
    }
}
