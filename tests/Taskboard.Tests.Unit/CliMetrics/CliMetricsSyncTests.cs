using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.CliMetrics;
using Taskboard.Dtos;
using Taskboard.Server.Services;
using Xunit;

namespace Taskboard.Tests.Unit.CliMetrics;

public class CliMetricsSyncTests
{
    private static (CliMetricsSyncCoordinator Coordinator, CountingService Service) CriarCoordinator(
        TimeSpan? delay = null)
    {
        var service = new CountingService(delay);
        var provider = new ServiceCollection()
            .AddScoped<ICliMetricsService>(_ => service)
            .BuildServiceProvider();
        return (new CliMetricsSyncCoordinator(provider.GetRequiredService<IServiceScopeFactory>()), service);
    }

    private sealed class CountingService : ICliMetricsService
    {
        private readonly TimeSpan? _delay;
        public int Calls;

        public CountingService(TimeSpan? delay) => _delay = delay;

        public async Task<CliMetricsSyncResultDto> SyncAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            if (_delay is not null)
            {
                await Task.Delay(_delay.Value, cancellationToken);
            }
            return new CliMetricsSyncResultDto("completed", false, 1, 0, null);
        }

        public Task<IReadOnlyList<CliMetricSourceDto>> GetSourcesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CliMetricSourceDto>>([]);
        public Task<CliMetricsSummaryDto> GetSummaryAsync(string? period, CancellationToken ct = default)
            => Task.FromResult(new CliMetricsSummaryDto("30d", new CliMetricsTotalsDto(0, 0, 0),
                new Dictionary<string, CliMetricsTotalsDto>(), []));
        public Task<IReadOnlyList<CliSessionMetricDto>> GetSessionsAsync(
            AgentCliKind? kind, DateTime? fromUtc, DateTime? toUtc, int take, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CliSessionMetricDto>>([]);
    }

    [Fact]
    public async Task Dado_SyncEmVoo_Quando_SegundaChamada_Entao_RetornaInFlight()
    {
        var (coordinator, service) = CriarCoordinator(delay: TimeSpan.FromMilliseconds(300));

        var first = coordinator.TrySyncAsync(CancellationToken.None);
        var second = await coordinator.TrySyncAsync(CancellationToken.None);
        await first;

        second.InFlight.ShouldBeTrue(customMessage: "segunda chamada não duplica o sync");
        service.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_SyncConcluido_Quando_NovaChamada_Entao_SincronizaNovamente()
    {
        var (coordinator, service) = CriarCoordinator();

        await coordinator.TrySyncAsync(CancellationToken.None);
        var segundo = await coordinator.TrySyncAsync(CancellationToken.None);

        segundo.InFlight.ShouldBeFalse();
        service.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task Dado_Disabled_Quando_HostedServiceInicia_Entao_NaoSincroniza()
    {
        var (coordinator, service) = CriarCoordinator();
        var hosted = new CliMetricsSyncService(
            coordinator,
            new CliMetricsOptions { Enabled = false },
            Substitute.For<ILogger<CliMetricsSyncService>>());

        await hosted.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await hosted.StopAsync(CancellationToken.None);

        service.Calls.ShouldBe(0, customMessage: "Enabled=false desliga a ingestão em background");
    }

    [Fact]
    public async Task Dado_Enabled_Quando_HostedServiceInicia_Entao_SincronizaNoStartup()
    {
        var (coordinator, service) = CriarCoordinator();
        var hosted = new CliMetricsSyncService(
            coordinator,
            new CliMetricsOptions { Enabled = true, SyncIntervalMinutes = 60 },
            Substitute.For<ILogger<CliMetricsSyncService>>());

        await hosted.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (service.Calls == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
        await hosted.StopAsync(CancellationToken.None);

        service.Calls.ShouldBe(1, customMessage: "primeira passagem roda no startup");
    }
}
