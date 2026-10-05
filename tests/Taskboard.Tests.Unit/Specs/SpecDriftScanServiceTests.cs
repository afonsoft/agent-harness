using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Application.Contracts.Jobs;
using Taskboard.Application.Contracts.Specs;
using Taskboard.Dtos;
using Taskboard.Server.Services;
using Taskboard.Tests.Unit.Jobs;
using Xunit;

namespace Taskboard.Tests.Unit.Specs;

/// <summary>SPEC-20260920-harness-maintenance-jobs RF-003 + SPEC-20261004-redis-hybrid-cache RF-005
/// — drift scan horário publica no cache HybridCache; diff "novos drifts" usa
/// snapshot process-local (<c>_previous</c>).</summary>
public sealed class SpecDriftScanServiceTests
{
    [Fact]
    public async Task Dado_CacheVazio_Quando_SetAsync_Entao_GetOrCreateServeSemFactory()
    {
        var cache = CreateCache();
        var report = new SpecDriftReportDto(10, 1, []);
        await cache.SetAsync(report, CancellationToken.None);

        var factoryRan = false;
        var served = await cache.GetOrCreateAsync(ct =>
        {
            factoryRan = true;
            return new ValueTask<SpecDriftReportDto>(new SpecDriftReportDto(0, 0, []));
        }, CancellationToken.None);

        served.TotalSpecs.ShouldBe(report.TotalSpecs);
        factoryRan.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_DetectorComDrift_Quando_StartAsync_Entao_PrimeiraPassadaPublicaCache()
    {
        var report = new SpecDriftReportDto(10, 1,
        [
            new SpecDriftItemDto("SPEC-1", "Done", "Deprecated", "arquivos deletados", ["x.cs"])
        ]);
        var detector = Substitute.For<ISpecDriftDetector>();
        detector.BuildReportAsync(null, Arg.Any<CancellationToken>()).Returns(Task.FromResult(report));
        var cache = CreateCache();
        var service = new SpecDriftScanService(detector, cache, CreateRegistry(), NullLogger<SpecDriftScanService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            // Poll the facade: once SetAsync lands, GetOrCreateAsync serves the
            // report without running the probe factory.
            var served = false;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!served && DateTime.UtcNow < deadline)
            {
                var factoryRan = false;
                var value = await cache.GetOrCreateAsync(ct =>
                {
                    factoryRan = true;
                    return new ValueTask<SpecDriftReportDto>(new SpecDriftReportDto(0, 0, []));
                }, CancellationToken.None);
                // Cache round-trips via serializer — compare content, not identity.
                served = !factoryRan && value.TotalSpecs == report.TotalSpecs;
                if (!served)
                {
                    await Task.Delay(50);
                }
            }

            served.ShouldBeTrue();
            await detector.Received(1).BuildReportAsync(null, Arg.Any<CancellationToken>());
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Dado_DetectorFalhando_Quando_StartAsync_Entao_ServicoSobreviveECacheFicaVazio()
    {
        var detector = Substitute.For<ISpecDriftDetector>();
        detector.BuildReportAsync(null, Arg.Any<CancellationToken>())
            .Returns<Task<SpecDriftReportDto>>(_ => throw new InvalidOperationException("boom"));
        var cache = CreateCache();
        var service = new SpecDriftScanService(detector, cache, CreateRegistry(), NullLogger<SpecDriftScanService>.Instance);

        // A exceção do tick fica contida — Start/Stop não propagam.
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await service.StopAsync(CancellationToken.None);

        // Cache vazio: a probe factory é obrigada a rodar.
        var factoryRan = false;
        await cache.GetOrCreateAsync(ct =>
        {
            factoryRan = true;
            return new ValueTask<SpecDriftReportDto>(new SpecDriftReportDto(0, 0, []));
        }, CancellationToken.None);
        factoryRan.ShouldBeTrue();
    }

    private static SpecDriftReportCache CreateCache()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        return new SpecDriftReportCache(
            services.BuildServiceProvider().GetRequiredService<HybridCache>());
    }

    private static JobRegistry CreateRegistry() => JobRegistryTestHost.Create(
        definitions: new JobDefinition(SpecDriftScanService.JobKey, "Spec drift scan", "", 3600, 300));
}
