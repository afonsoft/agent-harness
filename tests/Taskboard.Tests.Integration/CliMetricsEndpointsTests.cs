using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Taskboard.Dtos;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260919-cli-metrics §5 — /api/local/cli-metrics endpoints.
/// The factory points Taskboard:HomeDir at an empty dir, so every source
/// resolves Missing and sync is a fast no-op.
/// </summary>
public class CliMetricsEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;

    public CliMetricsEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Dado_SyncManual_Quando_PostSync_Entao_RetornaResultado()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var result = await SyncWhenIdleAsync(client);

        result.SourcesSynced.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Dado_SyncFeito_Quando_GetSources_Entao_ListaFontesComStatus()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        await SyncWhenIdleAsync(client);

        var response = await client.GetAsync("/api/local/cli-metrics/sources");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sources = await response.Content.ReadFromJsonAsync<List<CliMetricSourceDto>>();
        sources.ShouldNotBeNull();
        sources.ShouldNotBeEmpty();
        sources.ShouldAllBe(s => s.Status == "Missing",
            customMessage: "home vazio → todas as fontes Missing");
    }

    [Fact]
    public async Task Dado_SemDados_Quando_GetSummary_Entao_200ComTotaisZerados()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/local/cli-metrics/summary?period=7d");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<CliMetricsSummaryDto>();
        summary.ShouldNotBeNull();
        summary.Period.ShouldBe("7d");
        summary.Totals.Sessions.ShouldBe(0);
    }

    [Fact]
    public async Task Dado_SemDados_Quando_GetSessions_Entao_200ListaVazia()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/local/cli-metrics/sessions?take=10");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sessions = await response.Content.ReadFromJsonAsync<List<CliSessionMetricDto>>();
        sessions.ShouldNotBeNull().ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_Anonimo_Quando_CliMetrics_Entao_NaoAutorizado()
    {
        var client = _factory.CreateClient();

        (await client.GetAsync("/api/local/cli-metrics/sources")).StatusCode
            .ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Redirect);
        (await client.PostAsync("/api/local/cli-metrics/sync", null)).StatusCode
            .ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Redirect);
    }

    // The hosted CliMetricsSyncService runs an initial pass on startup through
    // the same single-flight coordinator — a manual POST that lands during it
    // returns InFlight with zero sources. Retry on that real signal instead of
    // racing the startup pass.
    private static async Task<CliMetricsSyncResultDto> SyncWhenIdleAsync(HttpClient client)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            var response = await client.PostAsync("/api/local/cli-metrics/sync", null);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<CliMetricsSyncResultDto>();
            result.ShouldNotBeNull();
            if (!result.InFlight || DateTime.UtcNow >= deadline)
            {
                return result;
            }

            await Task.Delay(100);
        }
    }
}
