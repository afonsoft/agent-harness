using Microsoft.EntityFrameworkCore;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Harness;
using Taskboard.CliMetrics;
using Taskboard.Domain.Agents;
using Taskboard.Domain.Entities.CliMetrics;
using Taskboard.Domain.Entities.Harness;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.EntityFrameworkCore.Repositories;
using Taskboard.Harness;
using Taskboard.Harness.FinOps;
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
            new EfCoreRepository<CliDailyUsageAggregate>(_context),
            new EfCoreRepository<CliSessionMetric>(_context),
            new EfCoreRepository<CliMetricSource>(_context),
            new EfCoreRepository<AgentRun>(_context),
            new FinOpsOptions(),
            TimeProvider.System);
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

    private CliSessionMetric AddCliSession(
        string externalId, DateTime started, DateTime? ended = null, string? model = "gpt-5",
        long? input = 100, long? output = 50, AgentCliKind kind = AgentCliKind.Codex,
        bool tokensEstimated = false, int? messages = 3)
    {
        var session = CliSessionMetric.Create(
            CliMetricSourceId.NewGuid(), kind, externalId, "sessão " + externalId,
            started, ended, messages, model, input, output, null, DateTime.UtcNow, tokensEstimated);
        _context.CliSessionMetrics.Add(session);
        _context.SaveChanges();
        return session;
    }

    [Fact]
    public async Task Dado_UsoEmJanelas_Quando_Summary_Entao_TokenWindowsSomaCorretamente()
    {
        var now = DateTime.UtcNow;
        // Harness metrics: 1h ago, 3d ago, 10d ago, 40d ago.
        _context.RunCostMetrics.Add(new RunCostMetric(Guid.NewGuid(), "r1", AgentType.Claude, "m", 500, 500, 0, 0, 1m, now.AddHours(-1), null, null));
        _context.RunCostMetrics.Add(new RunCostMetric(Guid.NewGuid(), "r2", AgentType.Claude, "m", 1000, 1000, 0, 0, 1m, now.AddDays(-3), null, null));
        _context.RunCostMetrics.Add(new RunCostMetric(Guid.NewGuid(), "r3", AgentType.Claude, "m", 2000, 2000, 0, 0, 1m, now.AddDays(-10), null, null));
        // CLI sessions: 2h ago (300 tok) e 8d ago (1000 tok).
        AddCliSession("w1", now.AddHours(-2), input: 200, output: 100);
        AddCliSession("w2", now.AddDays(-8), input: 700, output: 300);
        _context.SaveChanges();

        var summary = await _service.GetSummaryAsync("all");

        summary.TokenWindows.ShouldNotBeNull();
        // 24h: métrica r1 (1000) + sessão w1 (300).
        summary.TokenWindows.Last24h.ShouldBe(1300);
        // 7d: r1+r2 (3000) + w1 (300).
        summary.TokenWindows.Last7d.ShouldBe(3300);
        // 30d: r1+r2+r3 (7000) + w1+w2 (1300).
        summary.TokenWindows.Last30d.ShouldBe(8300);
    }

    [Fact]
    public async Task Dado_SessoesCli_Quando_Summary_Entao_ModelUsagePorFonteModelo()
    {
        var now = DateTime.UtcNow;
        await _service.RecordUsageAsync("run-x", AgentType.Claude, "claude-3-7-sonnet", new TokenUsage(100, 50, 0, 0));
        AddCliSession("m1", now.AddHours(-1), model: "gpt-5.6", kind: AgentCliKind.Codex);
        AddCliSession("m2", now.AddHours(-2), model: "gpt-5.6", kind: AgentCliKind.Codex);
        AddCliSession("m3", now.AddHours(-3), model: null, kind: AgentCliKind.OpenCode);

        var summary = await _service.GetSummaryAsync("last-30-days");

        summary.ModelUsage.ShouldNotBeNull();
        summary.ModelUsage.ShouldContainKey("Harness · claude-3-7-sonnet");
        summary.ModelUsage.ShouldContainKey("Codex · gpt-5.6");
        summary.ModelUsage.ShouldContainKey("OpenCode · (unknown)");
        summary.ModelUsage["Codex · gpt-5.6"].ShouldBe(50.0, 0.01);
        summary.ModelUsage.Values.Sum().ShouldBe(100.0, 0.01);
    }

    [Fact]
    public async Task Dado_Periodo24h_Quando_Summary_Entao_24BinsHorarios()
    {
        var summary = await _service.GetSummaryAsync("24h");

        summary.ActivityBins.ShouldNotBeNull();
        summary.ActivityBins.Count.ShouldBe(24);
        // Série contínua — todos os bins emitidos, mesmo zerados.
        summary.ActivityBins.Sum(b => b.Sessions).ShouldBe(0);
    }

    [Fact]
    public async Task Dado_Periodo30d_Quando_Summary_Entao_BinsDiariosAte40()
    {
        var summary = await _service.GetSummaryAsync("last-30-days");

        summary.ActivityBins.ShouldNotBeNull();
        summary.ActivityBins.Count.ShouldBeLessThanOrEqualTo(40);
        summary.ActivityBins.Count.ShouldBe(30);
    }

    [Fact]
    public async Task Dado_PeriodoAll_Quando_Summary_Entao_BinsCapadosEm40()
    {
        var summary = await _service.GetSummaryAsync("all");

        summary.ActivityBins.ShouldNotBeNull();
        summary.ActivityBins.Count.ShouldBeLessThanOrEqualTo(40);
    }

    [Fact]
    public async Task Dado_SessaoAtivaRecente_Quando_Summary_Entao_StatusRunning()
    {
        var now = DateTime.UtcNow;
        AddCliSession("recent-1", now.AddMinutes(-40), ended: now.AddMinutes(-10));

        var summary = await _service.GetSummaryAsync("last-30-days");

        var row = summary.RecentSessions.ShouldNotBeNull().Single(r => r.Id == "recent-1");
        row.Status.ShouldBe("running");
        row.Source.ShouldBe("Codex");
    }

    [Fact]
    public async Task Dado_SessaoAntiga_Quando_Summary_Entao_StatusFinished()
    {
        var now = DateTime.UtcNow;
        AddCliSession("old-1", now.AddHours(-3), ended: now.AddMinutes(-40));

        var summary = await _service.GetSummaryAsync("last-30-days");

        var row = summary.RecentSessions.ShouldNotBeNull().Single(r => r.Id == "old-1");
        row.Status.ShouldBe("finished");
    }

    [Fact]
    public async Task Dado_SemAtividade_Quando_Summary_Entao_AlertaSemSessaoAtiva()
    {
        var summary = await _service.GetSummaryAsync("last-30-days");

        summary.Alerts.ShouldNotBeNull()
            .ShouldContain(a => a.Code == "NoActiveSessions" && a.Severity == "warn");
    }

    [Fact]
    public async Task Dado_FonteDrifted_Quando_Summary_Entao_AlertaSchemaDrift()
    {
        var now = DateTime.UtcNow;
        var source = CliMetricSource.Register(AgentCliKind.Codex, "logs_2.sqlite", "~/.codex/logs_2.sqlite", now);
        source.MarkSync(CliDbSourceStatus.SchemaDrifted, null, 0, now);
        _context.CliMetricSources.Add(source);
        _context.SaveChanges();

        var summary = await _service.GetSummaryAsync("last-30-days");

        summary.Alerts.ShouldNotBeNull()
            .ShouldContain(a => a.Code == "SourceSchemaDrifted" && a.Severity == "warn");
    }

    [Fact]
    public async Task Dado_RunBudgetExceeded_Quando_Summary_Entao_AlertaCrit()
    {
        var now = DateTime.UtcNow;
        var run = new AgentRun(Guid.NewGuid(), "issue-42", AgentType.Claude, now.AddHours(-2));
        run.MarkRunning();
        run.MarkFinished(AgentRunState.BudgetExceeded, now.AddHours(-1));
        _context.AgentRuns.Add(run);
        _context.SaveChanges();

        var summary = await _service.GetSummaryAsync("last-30-days");

        summary.Alerts.ShouldNotBeNull()
            .ShouldContain(a => a.Code == "BudgetExceeded" && a.Severity == "crit");
    }

    [Fact]
    public async Task Dado_CustoDiarioAcimaDoDobro_Quando_Summary_Entao_AlertaSpike()
    {
        var now = DateTime.UtcNow;
        // Média diária baixa (dias anteriores baratos) + hoje estourando.
        _context.RunCostMetrics.Add(new RunCostMetric(Guid.NewGuid(), "lo", AgentType.Claude, "m", 0, 0, 0, 0, 0.10m, now.AddDays(-10), null, null));
        _context.RunCostMetrics.Add(new RunCostMetric(Guid.NewGuid(), "hi", AgentType.Claude, "m", 0, 0, 0, 0, 5.00m, now, null, null));
        _context.SaveChanges();

        var summary = await _service.GetSummaryAsync("last-30-days");

        // avg = 5.10/30 ≈ 0.17; hoje 5.00 > 2×0.17 → alerta.
        summary.Alerts.ShouldNotBeNull()
            .ShouldContain(a => a.Code == "DailyCostSpike" && a.Severity == "warn");
    }

    [Fact]
    public async Task Dado_CustoExatamenteNoDobro_Quando_Summary_Entao_SemAlertaSpike()
    {
        var now = DateTime.UtcNow;
        // 30 dias: 29 dias a 0 + hoje X → média = X/30; X > 2X/30 sempre. Para
        // testar a fronteira, semear custo distribuído tal que hoje == 2×média:
        // hoje 2.00, 29 dias restantes total 28.00 → média = 1.00; 2.00 == 2×1.00 → sem alerta.
        _context.RunCostMetrics.Add(new RunCostMetric(Guid.NewGuid(), "today", AgentType.Claude, "m", 0, 0, 0, 0, 2.00m, now, null, null));
        _context.RunCostMetrics.Add(new RunCostMetric(Guid.NewGuid(), "past", AgentType.Claude, "m", 0, 0, 0, 0, 28.00m, now.AddDays(-15), null, null));
        _context.SaveChanges();

        var summary = await _service.GetSummaryAsync("last-30-days");

        summary.Alerts.ShouldNotBeNull()
            .ShouldNotContain(a => a.Code == "DailyCostSpike");
    }

    [Fact]
    public async Task Dado_SessaoEstimada_Quando_Summary_Entao_BadgeEstimado()
    {
        var now = DateTime.UtcNow;
        AddCliSession("est-1", now.AddHours(-1), tokensEstimated: true);
        var real = AddCliSession("real-1", now.AddHours(-2), tokensEstimated: false, model: "claude-3-7-sonnet");
        real.SetCost(0.01m); // rate existe → não estimado
        _context.SaveChanges();

        var summary = await _service.GetSummaryAsync("last-30-days");

        var recent = summary.RecentSessions.ShouldNotBeNull();
        recent.Single(r => r.Id == "est-1").Estimated.ShouldBeTrue();
        recent.Single(r => r.Id == "real-1").Estimated.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_SessaoComFallbackDePreco_Quando_Summary_Entao_Estimated()
    {
        var now = DateTime.UtcNow;
        // Modelo sem ModelPriceRate + custo > 0 → fallback $9.5/1M → badge ~.
        var s = AddCliSession("fb-1", now.AddHours(-1), model: "modelo-sem-rate", tokensEstimated: false);
        s.SetCost(1.00m);
        _context.SaveChanges();

        var summary = await _service.GetSummaryAsync("last-30-days");

        var row = summary.RecentSessions.ShouldNotBeNull().Single(r => r.Id == "fb-1");
        row.Estimated.ShouldBeTrue();
    }
}
