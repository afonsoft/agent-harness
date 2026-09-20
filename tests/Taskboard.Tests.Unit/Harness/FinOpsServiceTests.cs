using Microsoft.EntityFrameworkCore;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Harness;
using Taskboard.CliMetrics;
using Taskboard.Domain.Entities.CliMetrics;
using Taskboard.Domain.Entities.Harness;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.EntityFrameworkCore.Repositories;
using Taskboard.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

public sealed class FinOpsServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TaskboardDbContext _context;
    private readonly FinOpsService _service;

    public FinOpsServiceTests()
    {
        _dbPath = Path.Join(Path.GetTempPath(), $"tb-finops-{Guid.NewGuid()}.sqlite");
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        _context = new TaskboardDbContext(options);
        _context.Database.EnsureCreated();
        _service = new FinOpsService(
            new EfCoreRepository<RunCostMetric>(_context),
            new EfCoreRepository<ModelPriceRate>(_context),
            new EfCoreRepository<CliDailyUsageAggregate>(_context));
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
    public async Task Dado_UsoClaude37_Quando_RecordUsage_Entao_CustoExatoDoSeed()
    {
        // AC1: 10k in * $3/1M + 2k out * $15/1M = $0.06 (seeded rate).
        var metric = await _service.RecordUsageAsync(
            "run-1", AgentType.Claude, "claude-3-7-sonnet", new TokenUsage(10_000, 2_000, 0, 0));

        metric.CostUsd.ShouldBe(0.06m);
        metric.RunId.ShouldBe("run-1");
    }

    [Fact]
    public async Task Dado_DoisRegistros_Quando_CumulativeCost_Entao_Soma()
    {
        await _service.RecordUsageAsync("run-2", AgentType.Claude, "claude-3-7-sonnet", new TokenUsage(10_000, 2_000, 0, 0));
        await _service.RecordUsageAsync("run-2", AgentType.Codex, "gpt-5", new TokenUsage(1_000_000, 0, 0, 0), stageKey: "fix");

        var cumulative = await _service.GetCumulativeCostAsync("run-2");

        cumulative.ShouldBe(0.06m + 1.25m);
    }

    [Fact]
    public async Task Dado_Metricas_Quando_Summary_Entao_AgregaPorAgenteEModelo()
    {
        await _service.RecordUsageAsync("run-a", AgentType.Claude, "claude-3-7-sonnet", new TokenUsage(1_000_000, 0, 0, 0));
        await _service.RecordUsageAsync("run-b", AgentType.Codex, "gpt-5", new TokenUsage(0, 1_000_000, 0, 0));

        var summary = await _service.GetSummaryAsync("last-30-days");

        summary.RunsCount.ShouldBe(2);
        summary.TotalTokens.ShouldBe(2_000_000);
        summary.TotalCostUsd.ShouldBe(3.00m + 10.00m);
        summary.CostByAgent["Claude"].ShouldBe(3.00m);
        summary.CostByAgent["Codex"].ShouldBe(10.00m);
        summary.CostByModel["claude-3-7-sonnet"].ShouldBe(3.00m);
        summary.DailyCosts.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Dado_RunSemMetricas_Quando_Telemetry_Entao_Nulo()
    {
        (await _service.GetRunTelemetryAsync("inexistente")).ShouldBeNull();
    }

    [Fact]
    public async Task Dado_RunComMetricas_Quando_Telemetry_Entao_TotaisEDuracao()
    {
        await _service.RecordUsageAsync("run-t", AgentType.Claude, "claude-3-7-sonnet", new TokenUsage(100, 50, 10, 5), budgetCapUsd: 1.50m);

        var telemetry = await _service.GetRunTelemetryAsync("run-t");

        telemetry.ShouldNotBeNull();
        telemetry.TotalTokens.ShouldBe(165);
        telemetry.InputTokens.ShouldBe(100);
        telemetry.OutputTokens.ShouldBe(50);
        telemetry.CacheTokens.ShouldBe(15);
        telemetry.BudgetCapUsd.ShouldBe(1.50m);
        telemetry.Metrics.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_AgregadosCli_Quando_Summary_Entao_CliUsageProjetado()
    {
        // SPEC-20260920 RF-004 — seção cliUsage vem dos agregados diários CLI.
        var now = DateTime.UtcNow;
        var day = now.ToString("yyyy-MM-dd");
        var agg = CliDailyUsageAggregate.Register(AgentCliKind.OpenCode, day, now);
        agg.Add(sessions: 3, messages: 20, tokensIn: 1_000_000, tokensOut: 50_000,
            tokensCached: 900_000, modelName: "Opus", now);
        agg.SetCost(4.25m, now);
        _context.CliDailyUsageAggregates.Add(agg);
        _context.SaveChanges();

        var summary = await _service.GetSummaryAsync("last-30-days");

        summary.CliUsage.ShouldNotBeNull();
        summary.CliUsage.Sessions.ShouldBe(3);
        summary.CliUsage.TokensInput.ShouldBe(1_000_000);
        summary.CliUsage.CostUsd.ShouldBe(4.25m);
        summary.CliUsage.CostByCli["OpenCode"].ShouldBe(4.25m);
    }

    [Fact]
    public async Task Dado_SemAgregadosCli_Quando_Summary_Entao_CliUsageNulo()
    {
        var summary = await _service.GetSummaryAsync("all");

        summary.CliUsage.ShouldBeNull();
    }
}
