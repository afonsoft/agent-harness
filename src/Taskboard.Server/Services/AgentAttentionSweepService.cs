using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Server.Services;

/// <summary>
/// SPEC-20261020-agent-attention-model RF-010: periodic sweep marking silent
/// sessions <c>unverifiable</c> — never <c>exited</c> (silence ≠ death).
/// Interval/window via <c>Taskboard:AgentAttention:*</c>.
/// </summary>
public sealed class AgentAttentionSweepService : BackgroundService
{
    private readonly IAgentAttentionService _attention;
    private readonly ILogger<AgentAttentionSweepService> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _window;

    public AgentAttentionSweepService(
        IAgentAttentionService attention,
        IConfiguration configuration,
        ILogger<AgentAttentionSweepService> logger)
    {
        _attention = attention;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(
            Math.Max(10, configuration.GetValue("Taskboard:AgentAttention:SweepIntervalSeconds", 60)));
        _window = TimeSpan.FromSeconds(
            Math.Max(30, configuration.GetValue("Taskboard:AgentAttention:UnverifiableAfterSeconds", 300)));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var swept = await _attention.SweepUnverifiableAsync(_window, stoppingToken)
                    .ConfigureAwait(false);
                if (swept > 0)
                {
                    _logger.LogInformation("attention sweep marked {Count} session(s) unverifiable", swept);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "attention sweep tick failed");
            }
        }
    }
}
