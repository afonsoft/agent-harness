using Microsoft.EntityFrameworkCore;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Harness;
using Taskboard.CliMetrics;
using Taskboard.Domain.Entities.CliMetrics;
using Taskboard.Domain.Entities.Harness;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.EntityFrameworkCore.Repositories;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

/// <summary>SPEC-20260920-harness-recurring-jobs RF-001/RF-003 — projeção de custo sobre uso CLI.</summary>
public sealed class FinOpsAggregatorTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly CliMetricSourceId SourceId = CliMetricSourceId.NewGuid();

    private readonly string _dbPath;
    private readonly TaskboardDbContext _context;
    private readonly FinOpsAggregator _aggregator;

    public FinOpsAggregatorTests()
    {
        _dbPath = Path.Join(Path.GetTempPath(), $"tb-finopsagg-{Guid.NewGuid()}.sqlite");
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        _context = new TaskboardDbContext(options);
        _context.Database.EnsureCreated();
        _aggregator = new FinOpsAggregator(
            new EfCoreRepository<CliSessionMetric>(_context),
            new EfCoreRepository<CliDailyUsageAggregate>(_context),
            new EfCoreRepository<ModelPriceRate>(_context));
    }

    public void Dispose()
    {
        _context.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private CliSessionMetric AddSession(
        string externalId, string? model, long? input, long? output, long? cached = null)
    {
        var session = CliSessionMetric.Create(
            SourceId, AgentCliKind.OpenCode, externalId, "t",
            Now.AddHours(-1), Now, 10, model, input, output, cached, Now);
        _context.CliSessionMetrics.Add(session);
        _context.SaveChanges();
        return session;
    }

    [Fact]
    public async Task Dado_SessaoClaude_Quando_RunOnce_Entao_CustoExatoDoSeed()
    {
        // 10k in * $3/1M + 2k out * $15/1M = $0.06 (seeded claude-3-7-sonnet).
        AddSession("s1", "claude-3-7-sonnet", 10_000, 2_000);

        var processed = await _aggregator.RunOnceAsync();

        processed.ShouldBe(1);
        var session = await _context.CliSessionMetrics.SingleAsync();
        session.CostUsd.ShouldBe(0.06m);
    }

    [Fact]
    public async Task Dado_EnvelopeJsonOpus_Quando_RunOnce_Entao_ResolveRateOmniroute()
    {
        // 1M in via omniroute "Opus" seed: $15/1M.
        AddSession("s2", """{"id":"Opus","providerID":"omniroute"}""", 1_000_000, 0);

        await _aggregator.RunOnceAsync();

        var session = await _context.CliSessionMetrics.SingleAsync();
        session.CostUsd.ShouldBe(15.00m);
    }

    [Fact]
    public async Task Dado_SessaoSemTokens_Quando_RunOnce_Entao_CustoZeroMarcado()
    {
        AddSession("s3", null, null, null);

        var processed = await _aggregator.RunOnceAsync();

        processed.ShouldBe(1);
        var session = await _context.CliSessionMetrics.SingleAsync();
        session.CostUsd.ShouldBe(0m);

        // Já costada — próxima passada não reprocessa.
        (await _aggregator.RunOnceAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Dado_SessaoCostada_Quando_RunOnce_Entao_Idempotente()
    {
        AddSession("s4", "claude-3-7-sonnet", 10_000, 2_000);
        await _aggregator.RunOnceAsync();

        var second = await _aggregator.RunOnceAsync();

        second.ShouldBe(0);
        var agg = await _context.CliDailyUsageAggregates.SingleAsync();
        agg.CostUsd.ShouldBe(0.06m); // delta adicionado uma única vez
    }

    [Fact]
    public async Task Dado_SessoesCostadas_Quando_RunOnce_Entao_DeltaNoAgregadoDiario()
    {
        AddSession("s5", "claude-3-7-sonnet", 10_000, 2_000);
        AddSession("s6", "claude-3-7-sonnet", 10_000, 2_000);

        await _aggregator.RunOnceAsync();

        var agg = await _context.CliDailyUsageAggregates.SingleAsync();
        agg.Kind.ShouldBe(AgentCliKind.OpenCode);
        agg.Day.ShouldBe(Now.AddHours(-1).ToString("yyyy-MM-dd"));
        agg.CostUsd.ShouldBe(0.12m);
    }

    [Fact]
    public async Task Dado_SessaoReingerida_Quando_Update_Entao_CustoResetaParaRecostar()
    {
        var session = AddSession("s7", "claude-3-7-sonnet", 10_000, 2_000);
        await _aggregator.RunOnceAsync();
        session.CostUsd.ShouldBe(0.06m);

        // Re-ingest com tokens novos reseta CostUsd → próximo tick re-costa.
        session.Update("t", Now, 10, "claude-3-7-sonnet", 20_000, 2_000, null, Now);
        _context.SaveChanges();
        session.CostUsd.ShouldBeNull();

        await _aggregator.RunOnceAsync();
        session.CostUsd.ShouldBe(0.09m); // 20k*$3 + 2k*$15
    }
}
