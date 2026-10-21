using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Integrations.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20261020-provider-quota-usage P3: Anthropic api/oauth/usage parse +
/// credential tolerance, api > cli > local_estimate precedence, provider-
/// filtered refresh, plan-limit merge/parse.
/// </summary>
public class UsageCollectorsP3Tests
{
    private static readonly DateTime Now =
        new(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);

    // ---------- AnthropicUsageParser ----------

    [Fact]
    public void Dado_UsagePayload_Quando_Parse_Entao_WindowsMedidas()
    {
        const string json = """
            {"five_hour":{"utilization":42.5,"resets_at":"2026-10-22T15:00:00Z"},
             "seven_day":{"utilization":61.0,"resets_at":"2026-10-29T00:00:00Z"},
             "seven_day_opus":{"utilization":10.0,"resets_at":"2026-10-29T00:00:00Z"}}
            """;

        var windows = AnthropicUsageParser.Parse(json, Now);

        windows.Count.ShouldBe(2);
        var five = windows.Single(w => w.WindowKind == ProviderWindowKinds.FiveHour);
        five.Provider.ShouldBe("claude");
        five.UsedPct.ShouldBe(42.5);
        five.ResetsAtUtc.ShouldBe(Now.AddHours(5));
        five.Source.ShouldBe(ProviderUsageSources.Api);
        five.Confidence.ShouldBe(ProviderUsageConfidences.Measured);
        // weekly buckets collapse to the highest utilization.
        var weekly = windows.Single(w => w.WindowKind == ProviderWindowKinds.Weekly);
        weekly.UsedPct.ShouldBe(61.0);
    }

    [Fact]
    public void Dado_UsageSemResets_Quando_Parse_Entao_ResetNulo()
    {
        const string json = """{"five_hour":{"utilization":7}}""";

        var w = AnthropicUsageParser.Parse(json, Now).Single();

        w.UsedPct.ShouldBe(7);
        w.ResetsAtUtc.ShouldBeNull();
    }

    [Fact]
    public void Dado_PayloadInvalido_Quando_Parse_Entao_Vazio()
    {
        AnthropicUsageParser.Parse("not-json", Now).ShouldBeEmpty();
        AnthropicUsageParser.Parse("""{"five_hour":"oops"}""", Now).ShouldBeEmpty();
        AnthropicUsageParser.Parse("""{"other":{"utilization":9}}""", Now)
            .ShouldBeEmpty();
        AnthropicUsageParser.Parse("", Now).ShouldBeEmpty();
    }

    // ---------- AnthropicUsageCollector ----------

    [Fact]
    public async Task Dado_SemCredencial_Quando_Collect_Entao_VazioSemHttp()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = new AnthropicUsageCollector(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/") },
            credentialsPath: Path.Combine(Path.GetTempPath(), "no-such-creds.json"),
            envToken: () => null, now: () => Now);

        (await sut.CollectAsync("local")).ShouldBeEmpty();
        handler.Calls.ShouldBe(0); // never sends an unauthenticated request.
    }

    [Fact]
    public async Task Dado_TokenEnv_Quando_Collect_Entao_BearerComBeta()
    {
        const string payload =
            """{"five_hour":{"utilization":88,"resets_at":"2026-10-22T15:00:00Z"}}""";
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload),
        });
        var sut = new AnthropicUsageCollector(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/") },
            credentialsPath: "/nonexistent",
            envToken: () => "sk-ant-oat-test", now: () => Now);

        var windows = await sut.CollectAsync("local");

        windows.Single().UsedPct.ShouldBe(88);
        handler.LastRequest!.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.ShouldBe("sk-ant-oat-test");
        handler.LastRequest.Headers
            .GetValues("anthropic-beta").ShouldContain("oauth-2025-04-20");
    }

    [Fact]
    public async Task Dado_CredenciaisJson_Quando_Collect_Entao_AccessToken()
    {
        var path = Path.Combine(Path.GetTempPath(), $"creds-{Guid.NewGuid():N}.json");
        try
        {
            // expires relative to the collector's injected clock, not UtcNow.
            var expiresAt = new DateTimeOffset(Now).AddHours(1)
                .ToUnixTimeMilliseconds();
            await File.WriteAllTextAsync(path,
                """{"claudeAiOauth":{"accessToken":"file-token","expiresAt":""" +
                expiresAt + "}}");
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"five_hour":{"utilization":12}}"""),
            });
            var sut = new AnthropicUsageCollector(
                new HttpClient(handler) { BaseAddress = new Uri("https://a/") },
                credentialsPath: path, envToken: () => null, now: () => Now);

            (await sut.CollectAsync("local")).Single().UsedPct.ShouldBe(12);
            handler.LastRequest!.Headers.Authorization!.Parameter
                .ShouldBe("file-token");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Dado_TokenExpirado_Quando_Collect_Entao_Vazio()
    {
        var path = Path.Combine(Path.GetTempPath(), $"creds-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path,
                """{"claudeAiOauth":{"accessToken":"old","expiresAt":1000}}""");
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
            var sut = new AnthropicUsageCollector(
                new HttpClient(handler) { BaseAddress = new Uri("https://a/") },
                credentialsPath: path, envToken: () => null, now: () => Now);

            (await sut.CollectAsync("local")).ShouldBeEmpty();
            handler.Calls.ShouldBe(0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Dado_HttpErro_Quando_Collect_Entao_Vazio(HttpStatusCode status)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status));
        var sut = new AnthropicUsageCollector(
            new HttpClient(handler) { BaseAddress = new Uri("https://a/") },
            credentialsPath: "/nonexistent",
            envToken: () => "tok", now: () => Now);

        (await sut.CollectAsync("local")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_FalhaRede_Quando_Collect_Entao_Vazio()
    {
        var handler = new StubHandler(
            _ => throw new HttpRequestException("offline"));
        var sut = new AnthropicUsageCollector(
            new HttpClient(handler) { BaseAddress = new Uri("https://a/") },
            credentialsPath: "/nonexistent",
            envToken: () => "tok", now: () => Now);

        (await sut.CollectAsync("local")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_JsonMalformado_Quando_Collect_Entao_Vazio()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{broken"),
        });
        var sut = new AnthropicUsageCollector(
            new HttpClient(handler) { BaseAddress = new Uri("https://a/") },
            credentialsPath: "/nonexistent",
            envToken: () => "tok", now: () => Now);

        (await sut.CollectAsync("local")).ShouldBeEmpty();
    }

    // ---------- api > cli > local_estimate precedence ----------

    [Fact]
    public async Task Dado_RowApiFresca_Quando_CliChega_Entao_NaoSobrescreve()
    {
        var h = new UsageTestHarness();
        h.AddCollector("claude", () => [Sample(ProviderUsageSources.Api, 90)]);
        h.AddCollector("claude", () => [Sample(ProviderUsageSources.Cli, 10)]);
        h.Repo.Rows["claude|fiveHour"] = Sample(ProviderUsageSources.Api, 80);

        await h.Service.CollectOnceAsync("local");

        // cli sample loses to the fresh api row AND to the new api upsert.
        h.Repo.Rows["claude|fiveHour"].UsedPct.ShouldBe(90);
        h.Repo.Rows["claude|fiveHour"].Source.ShouldBe(ProviderUsageSources.Api);
    }

    [Fact]
    public async Task Dado_RowCliFresca_Quando_ApiChega_Entao_Sobrescreve()
    {
        var h = new UsageTestHarness();
        h.AddCollector("claude", () => [Sample(ProviderUsageSources.Api, 95)]);
        h.Repo.Rows["claude|fiveHour"] = Sample(ProviderUsageSources.Cli, 20);

        await h.Service.CollectOnceAsync("local");

        h.Repo.Rows["claude|fiveHour"].UsedPct.ShouldBe(95);
        h.Repo.Rows["claude|fiveHour"].Source.ShouldBe(ProviderUsageSources.Api);
    }

    [Fact]
    public async Task Dado_FiltroProvider_Quando_CollectOnce_Entao_SoChamaUm()
    {
        var h = new UsageTestHarness();
        var claude = h.AddCollector("claude", () => [Sample(ProviderUsageSources.Cli, 5)]);
        var codex = h.AddCollector("codex", () => [Sample(ProviderUsageSources.Cli, 5)]);

        await h.Service.CollectOnceAsync("local", provider: "codex");

        claude.Calls.ShouldBe(0);
        codex.Calls.ShouldBe(1);
    }

    // ---------- ProviderPlanLimits ----------

    [Fact]
    public void Dado_JsonValido_Quando_Parse_Entao_MapAninhado()
    {
        var parsed = ProviderPlanLimits.ParseJson(
            """{"claude":{"daily":500000,"fiveHour":0,"bad":-3},"codex":{"weekly":9}}""");

        parsed["claude"].ShouldContainKeyAndValue("daily", 500000L);
        parsed["claude"].ShouldNotContainKey("fiveHour"); // non-positive dropped.
        parsed["claude"].ShouldNotContainKey("bad");
        parsed["codex"].ShouldContainKeyAndValue("weekly", 9L);
    }

    [Fact]
    public void Dado_JsonRuim_Quando_Parse_Entao_Vazio()
    {
        ProviderPlanLimits.ParseJson(null).ShouldBeEmpty();
        ProviderPlanLimits.ParseJson("").ShouldBeEmpty();
        ProviderPlanLimits.ParseJson("{oops").ShouldBeEmpty();
        ProviderPlanLimits.ParseJson("""{"claude":null}""").ShouldBeEmpty();
    }

    [Fact]
    public void Dado_MapSalvo_Quando_ToJson_Entao_RoundTrip()
    {
        var saved = new Dictionary<string, IReadOnlyDictionary<string, long>>
        {
            ["claude"] = new Dictionary<string, long> { ["daily"] = 42_000 },
        };

        var round = ProviderPlanLimits.ParseJson(ProviderPlanLimits.ToJson(saved));

        round["claude"].ShouldContainKeyAndValue("daily", 42_000L);
    }

    [Fact]
    public void Dado_MergeTresNiveis_Quando_EffectiveFor_Entao_SavedVence()
    {
        var configured = new Dictionary<string, long>
        {
            ["claude:daily"] = 111_000,
            ["claude:monthly"] = 999_000, // config-only window survives.
        };
        var saved = new Dictionary<string, IReadOnlyDictionary<string, long>>
        {
            ["claude"] = new Dictionary<string, long> { ["daily"] = 222_000 },
        };

        var eff = ProviderPlanLimits.EffectiveFor("claude", configured, saved);

        eff["fiveHour"].ShouldBe(700_000);   // default.
        eff["weekly"].ShouldBe(7_000_000);   // default.
        eff["monthly"].ShouldBe(999_000);    // config.
        eff["daily"].ShouldBe(222_000);      // saved wins over config.
    }

    [Fact]
    public async Task Dado_SavedPlanLimits_Quando_GetPlanLimits_Entao_SavedVsEffective()
    {
        var h = new UsageTestHarness();
        var view = await h.Service.SavePlanLimitsAsync("local",
            new Dictionary<string, IReadOnlyDictionary<string, long>>
            {
                ["claude"] = new Dictionary<string, long> { ["daily"] = 123_000 },
            });

        view.Saved["claude"].ShouldContainKeyAndValue("daily", 123_000L);
        view.Effective["claude"].ShouldContainKeyAndValue("daily", 123_000L);
        view.Effective["claude"].ShouldContainKeyAndValue("fiveHour", 700_000L);
    }

    private static ProviderUsageWindowDto Sample(string source, double pct) =>
        new("claude", ProviderWindowKinds.FiveHour, null, null, pct, null,
            DateTime.UtcNow, source, ProviderUsageConfidences.Measured);

    /// <summary>Minimal stub — records the last request, replays a canned answer.</summary>
    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        public HttpRequestMessage? LastRequest;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }
}
