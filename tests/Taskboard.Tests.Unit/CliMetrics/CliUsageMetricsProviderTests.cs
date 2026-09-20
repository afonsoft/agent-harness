using Microsoft.EntityFrameworkCore;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Domain.Entities.CliMetrics;
using Taskboard.EntityFrameworkCore.CliMetrics;
using Taskboard.EntityFrameworkCore.Data;
using Xunit;

namespace Taskboard.Tests.Unit.CliMetrics;

public class CliUsageMetricsProviderTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TaskboardDbContext _context;
    private readonly EfCoreCliUsageMetricsProvider _provider;

    public CliUsageMetricsProviderTests()
    {
        _dbPath = Path.Join(Path.GetTempPath(), $"tb-cliusage-{Guid.NewGuid()}.sqlite");
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        _context = new TaskboardDbContext(options);
        _context.Database.EnsureCreated();
        _provider = new EfCoreCliUsageMetricsProvider(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public async Task Dado_SessoesComModelo_Quando_GetUsage_Entao_AgrupaPorKindEModelo()
    {
        var now = new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
        var source = CliMetricSource.Register(AgentCliKind.Codex, "state", ".codex/state.db", now);
        _context.CliMetricSources.Add(source);
        await _context.SaveChangesAsync();

        _context.CliSessionMetrics.Add(CliSessionMetric.Create(
            source.Id, AgentCliKind.Codex, "s1", null, now.AddHours(-2), null, 5, "gpt-5", 100, 50, 10, now));
        _context.CliSessionMetrics.Add(CliSessionMetric.Create(
            source.Id, AgentCliKind.Codex, "s2", null, now.AddHours(-1), null, 3, "gpt-5", 200, 80, 0, now));
        _context.CliSessionMetrics.Add(CliSessionMetric.Create(
            source.Id, AgentCliKind.Codex, "s3", null, now.AddDays(-10), null, 1, "old", 1, 1, 0, now));
        await _context.SaveChangesAsync();

        var usage = await _provider.GetUsageAsync(
            AgentCliKind.Codex, now.AddDays(-1), now.AddDays(1));

        var gpt5 = usage.ShouldHaveSingleItem();
        gpt5.Model.ShouldBe("gpt-5");
        gpt5.Sessions.ShouldBe(2);
        gpt5.Messages.ShouldBe(8);
        gpt5.TokensInput.ShouldBe(300);
        gpt5.TokensOutput.ShouldBe(130);
        gpt5.TokensCached.ShouldBe(10);
    }
}
