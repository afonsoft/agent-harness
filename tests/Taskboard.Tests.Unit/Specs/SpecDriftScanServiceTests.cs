using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Application.Contracts.Specs;
using Taskboard.Dtos;
using Taskboard.Server.Services;
using Xunit;

namespace Taskboard.Tests.Unit.Specs;

/// <summary>SPEC-20260920-harness-maintenance-jobs RF-003 — drift scan horário + cache.</summary>
public sealed class SpecDriftScanServiceTests
{
    [Fact]
    public void Dado_CacheVazio_Quando_Update_Entao_LastRetornaRelatorio()
    {
        var cache = new SpecDriftReportCache();
        cache.Last.ShouldBeNull();

        var report = new SpecDriftReportDto(10, 1, []);
        cache.Update(report);

        cache.Last.ShouldBe(report);
    }

    [Fact]
    public async Task Dado_DetectorComDrift_Quando_StartAsync_Entao_PrimeiraPassadaAlimentaCache()
    {
        var report = new SpecDriftReportDto(10, 1,
        [
            new SpecDriftItemDto("SPEC-1", "Done", "Deprecated", "arquivos deletados", ["x.cs"])
        ]);
        var detector = Substitute.For<ISpecDriftDetector>();
        detector.BuildReportAsync(null, Arg.Any<CancellationToken>()).Returns(Task.FromResult(report));
        var cache = new SpecDriftReportCache();
        var service = new SpecDriftScanService(detector, cache, NullLogger<SpecDriftScanService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (cache.Last is null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            cache.Last.ShouldBe(report);
            await detector.Received(1).BuildReportAsync(null, Arg.Any<CancellationToken>());
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Dado_DetectorFalhando_Quando_StartAsync_Entao_ServicoSobrevive()
    {
        var detector = Substitute.For<ISpecDriftDetector>();
        detector.BuildReportAsync(null, Arg.Any<CancellationToken>())
            .Returns<Task<SpecDriftReportDto>>(_ => throw new InvalidOperationException("boom"));
        var cache = new SpecDriftReportCache();
        var service = new SpecDriftScanService(detector, cache, NullLogger<SpecDriftScanService>.Instance);

        // A exceção do tick fica contida — Start/Stop não propagam.
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await service.StopAsync(CancellationToken.None);

        cache.Last.ShouldBeNull();
    }
}
