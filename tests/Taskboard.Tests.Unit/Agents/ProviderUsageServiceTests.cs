using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Agents;
using Taskboard.Application.Contracts.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20261020-provider-quota-usage P1: claude /usage parsing, warn
/// hysteresis, roster grouping/staleness, n/a filtering, collector tolerance.
/// </summary>
public class ProviderUsageServiceTests
{
    private static DateTime Now => DateTime.UtcNow;

    // ---------- fakes ----------

    private sealed class FakeUsageRepository : IProviderUsageRepository
    {
        public readonly Dictionary<string, ProviderUsageWindowDto> Rows =
            new(StringComparer.Ordinal);

        public Task<ProviderUsageWindowDto?> GetAsync(
            string provider, string kind, CancellationToken ct = default) =>
            Task.FromResult(Rows.GetValueOrDefault($"{provider}|{kind}"));

        public Task<IReadOnlyList<ProviderUsageWindowDto>> ListAsync(
            string? provider = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProviderUsageWindowDto>>(
                [.. Rows.Values.Where(w => provider is null || w.Provider == provider)]);

        public Task<ProviderUsageWindowDto> UpsertAsync(
            ProviderUsageWindowDto sample, CancellationToken ct = default)
        {
            var key = $"{sample.Provider}|{sample.WindowKind}";
            var prior = Rows.GetValueOrDefault(key);
            var stored = sample with { WarnLevel = prior?.WarnLevel ?? 0 };
            Rows[key] = stored;
            return Task.FromResult(stored);
        }

        public Task SetWarnLevelAsync(
            string provider, string kind, int level, CancellationToken ct = default)
        {
            var key = $"{provider}|{kind}";
            if (Rows.TryGetValue(key, out var row))
            {
                Rows[key] = row with { WarnLevel = level };
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeCollector(
        string provider,
        Func<IReadOnlyList<ProviderUsageWindowDto>> produce) : IProviderUsageCollector
    {
        public string Provider { get; } = provider;
        public int Calls;
        public Task<IReadOnlyList<ProviderUsageWindowDto>> CollectAsync(
            CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(produce());
        }
    }

    private sealed class Harness
    {
        public readonly FakeUsageRepository Repo = new();
        public readonly IAgentFeedService Feed = Substitute.For<IAgentFeedService>();
        public readonly List<IProviderUsageCollector> Collectors = new();

        // Lazy — the service snapshots the collector list at ctor time, so it
        // must be created after tests register their collectors.
        private ProviderUsageService? _service;
        public ProviderUsageService Service => _service ??= CreateService();

        private ProviderUsageService CreateService()
        {
            var services = new ServiceCollection()
                .AddScoped<IProviderUsageRepository>(_ => Repo)
                .AddScoped(_ => Feed)
                .BuildServiceProvider();
            return new ProviderUsageService(services, Collectors,
                NullLogger<ProviderUsageService>.Instance);
        }

        public FakeCollector AddCollector(
            string provider, Func<IReadOnlyList<ProviderUsageWindowDto>> produce)
        {
            var c = new FakeCollector(provider, produce);
            Collectors.Add(c);
            return c;
        }
    }

    private static ProviderUsageWindowDto Sample(
        double pct, string provider = "claude", string kind = "fiveHour",
        int warnLevel = 0) =>
        new(provider, kind, null, null, pct, null, Now,
            ProviderUsageSources.Cli, ProviderUsageConfidences.Measured,
            null, warnLevel);

    // ---------- collect/upsert ----------

    [Fact]
    public async Task Dado_Collector_Quando_Tick_Entao_UpsertWindows()
    {
        var h = new Harness();
        h.AddCollector("claude", () => [Sample(42), Sample(10, kind: "weekly")]);

        var n = await h.Service.CollectOnceAsync("local");

        n.ShouldBe(2);
        h.Repo.Rows.Count.ShouldBe(2);
        h.Repo.Rows["claude|fiveHour"].UsedPct.ShouldBe(42);
    }

    [Fact]
    public async Task Dado_CollectorFalha_Quando_Tick_Entao_Tolerante()
    {
        var h = new Harness();
        h.AddCollector("claude", () => throw new InvalidOperationException("offline"));
        h.AddCollector("codex", () => [Sample(20, provider: "codex", kind: "daily")]);

        var n = await h.Service.CollectOnceAsync("local");

        n.ShouldBe(1);
        h.Repo.Rows.ShouldContainKey("codex|daily");
    }

    [Fact]
    public async Task Dado_JanelaNa_Quando_Tick_Entao_NaoPersiste()
    {
        var h = new Harness();
        h.AddCollector("opencode", () =>
        [
            new ProviderUsageWindowDto("opencode", ProviderWindowKinds.Na,
                -1, null, null, null, Now,
                ProviderUsageSources.LocalEstimate,
                ProviderUsageConfidences.Estimated),
        ]);

        var n = await h.Service.CollectOnceAsync("local");

        n.ShouldBe(0);
        h.Repo.Rows.ShouldBeEmpty();
    }

    // ---------- warn hysteresis (RF-007) ----------

    [Fact]
    public async Task Dado_Pct79Para81_Quando_Tick_Entao_EmiteWarningUmaVez()
    {
        var h = new Harness();
        h.AddCollector("claude", () => [Sample(81)]);
        h.Repo.Rows["claude|fiveHour"] = Sample(79);

        await h.Service.CollectOnceAsync("local");

        await h.Feed.Received(1).ProduceAsync(
            Arg.Is<FeedItemRequest>(r => r.Kind == AgentFeedKind.QuotaWarning),
            Arg.Any<CancellationToken>());
        h.Repo.Rows["claude|fiveHour"].WarnLevel.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_Pct81Para85JaWarned_Quando_Tick_Entao_NaoReemite()
    {
        var h = new Harness();
        h.AddCollector("claude", () => [Sample(85)]);
        h.Repo.Rows["claude|fiveHour"] = Sample(81, warnLevel: 1);

        await h.Service.CollectOnceAsync("local");

        await h.Feed.DidNotReceive().ProduceAsync(
            Arg.Is<FeedItemRequest>(r => r.Kind == AgentFeedKind.QuotaWarning),
            Arg.Any<CancellationToken>());
        h.Repo.Rows["claude|fiveHour"].WarnLevel.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_Pct100_Quando_Tick_Entao_EmiteExhaustedNeedsYou()
    {
        var h = new Harness();
        h.AddCollector("claude", () => [Sample(100)]);
        h.Repo.Rows["claude|fiveHour"] = Sample(81, warnLevel: 1);

        await h.Service.CollectOnceAsync("local");

        await h.Feed.Received(1).ProduceAsync(
            Arg.Is<FeedItemRequest>(r =>
                r.Kind == AgentFeedKind.QuotaExhausted && r.NeedsAttention),
            Arg.Any<CancellationToken>());
        h.Repo.Rows["claude|fiveHour"].WarnLevel.ShouldBe(2);
    }

    [Fact]
    public async Task Dado_PctCai75_Quando_Tick_Entao_ReArma()
    {
        var h = new Harness();
        h.AddCollector("claude", () => [Sample(70)]);
        h.Repo.Rows["claude|fiveHour"] = Sample(81, warnLevel: 1);

        await h.Service.CollectOnceAsync("local");

        h.Repo.Rows["claude|fiveHour"].WarnLevel.ShouldBe(0);
    }

    [Fact]
    public async Task Dado_RearmadoDepoisSobe_Quando_Tick_Entao_EmiteDeNovo()
    {
        var h = new Harness();
        h.AddCollector("claude", () => [Sample(82)]);
        h.Repo.Rows["claude|fiveHour"] = Sample(70, warnLevel: 0);

        await h.Service.CollectOnceAsync("local");

        await h.Feed.Received(1).ProduceAsync(
            Arg.Is<FeedItemRequest>(r => r.Kind == AgentFeedKind.QuotaWarning),
            Arg.Any<CancellationToken>());
    }

    // ---------- roster / stale ----------

    [Fact]
    public async Task Dado_Windows_Quando_Roster_Entao_AgrupaPorProviderEMarcaStale()
    {
        var h = new Harness();
        h.Repo.Rows["claude|fiveHour"] = Sample(50);
        h.Repo.Rows["claude|weekly"] = Sample(12, kind: "weekly");
        h.Repo.Rows["codex|daily"] = Sample(30, provider: "codex", kind: "daily");
        h.Repo.Rows["codex|daily"] =
            h.Repo.Rows["codex|daily"] with { SampledAtUtc = Now.AddMinutes(-20) };

        var roster = await h.Service.GetRosterAsync();

        roster.Count.ShouldBe(2);
        var claude = roster.Single(r => r.Provider == "claude");
        claude.Windows.Count.ShouldBe(2);
        claude.Stale.ShouldBeFalse();
        roster.Single(r => r.Provider == "codex").Stale.ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_ProviderDesconhecido_Quando_Detail_Entao_Null()
    {
        var h = new Harness();

        (await h.Service.GetProviderAsync("nope")).ShouldBeNull();
    }
}
