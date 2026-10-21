using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.CliMetrics;
using Taskboard.Integrations.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20261020-provider-quota-usage P2: Codex rollout rate_limits parse,
/// Copilot quota_snapshots parse, local_estimate fallback + measured/estimated
/// precedence, RecentPcts history cap.
/// </summary>
public class UsageCollectorsP2Tests
{
    private static readonly DateTime Now =
        new(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);

    // ---------- CodexUsageParser ----------

    [Fact]
    public void Dado_RolloutComRateLimits_Quando_Parse_Entao_WindowsPorBucket()
    {
        const string tail = """
            {"timestamp":"2026-10-21T09:00:00Z","type":"event_msg","payload":{"type":"token_count","info":{"rate_limits":{"primary":{"used_percent":41.5,"window_minutes":300,"resets_in_seconds":7200},"secondary":{"used_percent":12.0,"window_minutes":10080,"resets_in_seconds":300000}}}}}
            {"timestamp":"2026-10-21T09:30:00Z","type":"event_msg","payload":{"type":"token_count","info":{"rate_limits":{"primary":{"used_percent":55.0,"window_minutes":300,"resets_in_seconds":5400},"secondary":{"used_percent":13.0,"window_minutes":10080,"resets_in_seconds":290000}}}}}
            """;

        var windows = CodexUsageParser.Parse(tail, Now);

        windows.Count.ShouldBe(2);
        // last turn wins per kind.
        var five = windows.Single(w => w.WindowKind == ProviderWindowKinds.FiveHour);
        five.UsedPct.ShouldBe(55.0);
        five.ResetsAtUtc.ShouldBe(Now.AddSeconds(5400));
        five.Source.ShouldBe(ProviderUsageSources.Cli);
        windows.Single(w => w.WindowKind == ProviderWindowKinds.Weekly)
            .UsedPct.ShouldBe(13.0);
    }

    [Fact]
    public void Dado_LinhasCorrompidas_Quando_Parse_Entao_Tolerante()
    {
        const string tail = """
            not-json at all
            {"broken": "rate_limits
            {"rate_limits":{"primary":{"used_percent":7.5,"window_minutes":300,"resets_in_seconds":600}}}
            """;

        var windows = CodexUsageParser.Parse(tail, Now);

        windows.Single().UsedPct.ShouldBe(7.5);
    }

    [Fact]
    public void Dado_SemRateLimits_Quando_Parse_Entao_Vazio()
    {
        const string tail =
            """{"type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"total_tokens":42}}}}""";

        CodexUsageParser.Parse(tail, Now).ShouldBeEmpty();
        CodexUsageParser.Parse("", Now).ShouldBeEmpty();
    }

    [Fact]
    public void Dado_WindowMinutesBuckets_Quando_Parse_Entao_MapeiaKind()
    {
        const string tail =
            """{"rate_limits":{"hourly":{"used_percent":9,"window_minutes":60,"resets_in_seconds":900},"day":{"used_percent":33,"window_minutes":1440,"resets_in_seconds":36000}}}""";

        var windows = CodexUsageParser.Parse(tail, Now);

        windows.Single(w => w.UsedPct == 9)
            .WindowKind.ShouldBe(ProviderWindowKinds.Rate);
        windows.Single(w => w.UsedPct == 33)
            .WindowKind.ShouldBe(ProviderWindowKinds.Daily);
    }

    // ---------- CopilotUsageParser ----------

    [Fact]
    public void Dado_QuotaSnapshots_Quando_Parse_Entao_MonthlyMaiorPressao()
    {
        const string json = """
            {"quota_snapshots":{
              "chat":{"percent_remaining":85,"reset_date":"2026-11-01","unlimited":false},
              "completions":{"percent_remaining":3,"reset_date":"2026-11-01","unlimited":false},
              "premium_interactions":{"percent_remaining":40,"reset_date":"2026-11-01","unlimited":false}}}
            """;

        var window = CopilotUsageParser.Parse(json, Now).Single();

        window.Provider.ShouldBe("copilot");
        window.WindowKind.ShouldBe(ProviderWindowKinds.Monthly);
        window.UsedPct.ShouldBe(97); // completions: 100 - 3 — highest pressure.
        window.ResetsAtUtc.ShouldBe(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        window.Source.ShouldBe(ProviderUsageSources.Api);
    }

    [Fact]
    public void Dado_SnapshotUnlimited_Quando_Parse_Entao_Ignora()
    {
        const string json = """
            {"quota_snapshots":{"completions":{"unlimited":true,"percent_remaining":100}}}
            """;

        CopilotUsageParser.Parse(json, Now).ShouldBeEmpty();
    }

    [Fact]
    public void Dado_SemQuotas_Quando_Parse_Entao_Vazio()
    {
        CopilotUsageParser.Parse("""{"login":"user"}""", Now).ShouldBeEmpty();
        CopilotUsageParser.Parse("not-json", Now).ShouldBeEmpty();
    }

    // ---------- LocalTokenEstimateCollector ----------

    [Fact]
    public async Task Dado_TokensNaJanela_Quando_Collect_Entao_PctEstimado()
    {
        var metrics = Substitute.For<ICliMetricsRepository>();
        metrics.SumTokensAsync(AgentCliKind.Claude,
                Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(350_000);
        var sp = new ServiceCollection()
            .AddScoped(_ => metrics).BuildServiceProvider();
        var config = new ConfigurationBuilder().Build();
        var sut = new LocalTokenEstimateCollector(
            sp, config, "claude", AgentCliKind.Claude);

        var windows = await sut.CollectAsync("local");

        // defaults: 700k fiveHour + 7M weekly.
        var five = windows.Single(w => w.WindowKind == ProviderWindowKinds.FiveHour);
        five.UsedPct.ShouldBe(50.0);
        five.Used.ShouldBe(350_000);
        five.Limit.ShouldBe(700_000);
        five.Confidence.ShouldBe(ProviderUsageConfidences.Estimated);
        five.Source.ShouldBe(ProviderUsageSources.LocalEstimate);
        windows.ShouldContain(w => w.WindowKind == ProviderWindowKinds.Weekly);
    }

    [Fact]
    public async Task Dado_ConfigOverride_Quando_Collect_Entao_UsaPlanLimits()
    {
        var metrics = Substitute.For<ICliMetricsRepository>();
        metrics.SumTokensAsync(Arg.Any<AgentCliKind>(),
                Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(90_000);
        var sp = new ServiceCollection()
            .AddScoped(_ => metrics).BuildServiceProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Taskboard:PlanLimits:claude:daily"] = "100000",
            }).Build();
        var sut = new LocalTokenEstimateCollector(
            sp, config, "claude", AgentCliKind.Claude);

        var windows = await sut.CollectAsync("local");

        // per-key merge: config adds `daily` over the default windows.
        var daily = windows.Single(w => w.WindowKind == ProviderWindowKinds.Daily);
        daily.UsedPct.ShouldBe(90);
        daily.Limit.ShouldBe(100_000);
        windows.ShouldContain(w => w.WindowKind == ProviderWindowKinds.FiveHour);
    }

    [Fact]
    public async Task Dado_ProviderSemDefaults_Quando_Collect_Entao_Vazio()
    {
        var metrics = Substitute.For<ICliMetricsRepository>();
        var sp = new ServiceCollection()
            .AddScoped(_ => metrics).BuildServiceProvider();
        var sut = new LocalTokenEstimateCollector(
            sp, new ConfigurationBuilder().Build(),
            "gemini", AgentCliKind.Kimi);

        (await sut.CollectAsync("local")).ShouldBeEmpty();
    }

    // ---------- measured vs estimated precedence (service) ----------

    [Fact]
    public async Task Dado_MeasuredFresco_Quando_EstimateChega_Entao_NaoSobrescreve()
    {
        var h = new UsageTestHarness();
        h.AddCollector("claude", () => [Measured(42)]);
        h.AddCollector("claude", () => [Estimated(99)]);
        h.Repo.Rows["claude|fiveHour"] = Measured(40); // fresh measured row.

        var n = await h.Service.CollectOnceAsync("local");

        h.Repo.Rows["claude|fiveHour"].UsedPct.ShouldBe(42);
        h.Repo.Rows["claude|fiveHour"].Confidence.ShouldBe(
            ProviderUsageConfidences.Measured);
        n.ShouldBe(1); // measured upserted, estimate skipped.
    }

    [Fact]
    public async Task Dado_MeasuredStale_Quando_EstimateChega_Entao_PreencheGap()
    {
        var h = new UsageTestHarness();
        var stale = Measured(80) with
        { SampledAtUtc = DateTime.UtcNow.AddMinutes(-30) };
        h.Repo.Rows["claude|fiveHour"] = stale;
        h.AddCollector("claude", () => []); // measured collector offline.
        h.AddCollector("claude", () => [Estimated(55)]);

        await h.Service.CollectOnceAsync("local");

        h.Repo.Rows["claude|fiveHour"].UsedPct.ShouldBe(55);
        h.Repo.Rows["claude|fiveHour"].Confidence.ShouldBe(
            ProviderUsageConfidences.Estimated);
    }

    private static Taskboard.Application.Contracts.Agents.ProviderUsageWindowDto Measured(
        double pct) =>
        new("claude", ProviderWindowKinds.FiveHour, null, null, pct, null,
            DateTime.UtcNow, ProviderUsageSources.Cli,
            ProviderUsageConfidences.Measured);

    private static Taskboard.Application.Contracts.Agents.ProviderUsageWindowDto Estimated(
        double pct) =>
        new("claude", ProviderWindowKinds.FiveHour, null, null, pct, null,
            DateTime.UtcNow, ProviderUsageSources.LocalEstimate,
            ProviderUsageConfidences.Estimated);
}
