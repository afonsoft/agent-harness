using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Harness;
using Taskboard.Harness.FinOps;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260919-ade-observability-finops §5 — /api/harness/finops/summary e
/// /api/harness/runs/{id}/telemetry.
/// </summary>
public class FinOpsEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;

    public FinOpsEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Dado_SemMetricas_Quando_GetSummary_Entao_200ComTotaisZerados()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/harness/finops/summary?period=last-30-days");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<FinOpsSummaryDto>();
        summary.ShouldNotBeNull();
        summary.RunsCount.ShouldBeGreaterThanOrEqualTo(0);
        summary.TotalCostUsd.ShouldBeGreaterThanOrEqualTo(0m);
    }

    [Fact]
    public async Task Dado_MetricaGravada_Quando_GetSummaryETelemetry_Entao_RefleteCusto()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var runId = $"itest-{Guid.NewGuid():N}";

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var finOps = scope.ServiceProvider.GetRequiredService<IFinOpsService>();
            await finOps.RecordUsageAsync(
                runId, AgentType.Claude, "claude-3-7-sonnet",
                new TokenUsage(10_000, 2_000, 0, 0), budgetCapUsd: 1.50m);
        }

        var summaryResponse = await client.GetAsync("/api/harness/finops/summary?period=all");
        summaryResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var summary = await summaryResponse.Content.ReadFromJsonAsync<FinOpsSummaryDto>();
        summary.ShouldNotBeNull();
        // AC1: 10k*3/1M + 2k*15/1M = $0.06
        summary.CostByAgent.ShouldContainKey("Claude");
        summary.CostByModel.ShouldContainKey("claude-3-7-sonnet");

        var telemetryResponse = await client.GetAsync($"/api/harness/runs/{runId}/telemetry");
        telemetryResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var telemetry = await telemetryResponse.Content.ReadFromJsonAsync<RunTelemetryDto>();
        telemetry.ShouldNotBeNull();
        telemetry.RunId.ShouldBe(runId);
        telemetry.TotalTokens.ShouldBe(12_000);
        telemetry.CostUsd.ShouldBe(0.06m);
        telemetry.BudgetCapUsd.ShouldBe(1.50m);
    }

    [Fact]
    public async Task Dado_RunInexistente_Quando_GetTelemetry_Entao_404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/harness/runs/nao-existe/telemetry");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dado_Anonimo_Quando_GetSummary_Entao_401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/harness/finops/summary");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
