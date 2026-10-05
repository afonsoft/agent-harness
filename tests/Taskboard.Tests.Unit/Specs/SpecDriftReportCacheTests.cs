using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Taskboard.Dtos;
using Taskboard.Server.Services;
using Xunit;

namespace Taskboard.Tests.Unit.Specs;

/// <summary>SPEC-20261004-redis-hybrid-cache RF-005 — fachada async sobre HybridCache.</summary>
public sealed class SpecDriftReportCacheTests
{
    [Fact]
    public async Task Dado_CacheVazio_Quando_DoisGetOrCreate_Entao_FactoryExecutaUmaVez()
    {
        var cache = CreateCache();
        var report = new SpecDriftReportDto(10, 1, []);
        var calls = 0;

        var first = await cache.GetOrCreateAsync(ct =>
        {
            calls++;
            return new ValueTask<SpecDriftReportDto>(report);
        }, CancellationToken.None);
        var second = await cache.GetOrCreateAsync(ct =>
        {
            calls++;
            return new ValueTask<SpecDriftReportDto>(report);
        }, CancellationToken.None);

        first.TotalSpecs.ShouldBe(report.TotalSpecs);
        second.TotalSpecs.ShouldBe(report.TotalSpecs);
        calls.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_RelatorioSetado_Quando_GetOrCreate_Entao_RetornaSemFactory()
    {
        var cache = CreateCache();
        var report = new SpecDriftReportDto(10, 2,
            [new SpecDriftItemDto("SPEC-1", "Done", "Deprecated", "gone", ["x.cs"])]);

        await cache.SetAsync(report, CancellationToken.None);

        var factoryRan = false;
        var served = await cache.GetOrCreateAsync(ct =>
        {
            factoryRan = true;
            return new ValueTask<SpecDriftReportDto>(new SpecDriftReportDto(0, 0, []));
        }, CancellationToken.None);

        served.TotalSpecs.ShouldBe(report.TotalSpecs);
        served.DriftItems.Count.ShouldBe(1);
        factoryRan.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_RelatorioAntigo_Quando_SetAsyncNovo_Entao_ServeONovo()
    {
        var cache = CreateCache();
        var old = new SpecDriftReportDto(10, 1, []);
        var fresh = new SpecDriftReportDto(10, 3, []);
        await cache.SetAsync(old, CancellationToken.None);

        await cache.SetAsync(fresh, CancellationToken.None);

        var served = await cache.GetOrCreateAsync(ct =>
            new ValueTask<SpecDriftReportDto>(old), CancellationToken.None);
        served.StaleSpecsCount.ShouldBe(fresh.StaleSpecsCount);
    }

    private static SpecDriftReportCache CreateCache()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        return new SpecDriftReportCache(
            services.BuildServiceProvider().GetRequiredService<HybridCache>());
    }
}
