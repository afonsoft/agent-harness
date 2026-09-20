using Taskboard.Application.Harness;

namespace Taskboard.Server.Services;

/// <summary>
/// Periodic pipeline tick — advances eligible stages and recovers executions
/// left mid-flight by a restart (SPEC-20260919-ade-multi-agent-orchestration RF-002).
/// </summary>
public sealed class PipelineEngineService(
    PipelineEngine engine,
    ILogger<PipelineEngineService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await TickAsync(stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TickAsync(stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken stoppingToken)
    {
        try
        {
            await engine.DispatchPendingAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Pipeline engine tick failed");
        }
    }
}
