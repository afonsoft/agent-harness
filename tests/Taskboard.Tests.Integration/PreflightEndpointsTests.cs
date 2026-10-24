using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Taskboard.Dtos;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20261023-p0-agent-hardening RF-C01/C03 — GET /api/harness/preflight
/// e o caminho dryRun de POST /api/harness/runs (resolve + veredito, nada
/// persiste, nada spawna).
/// </summary>
public class PreflightEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;

    public PreflightEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Dado_Servidor_Quando_GetPreflight_Entao_200ComVerdictEChecks()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/harness/preflight");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var report = await response.Content.ReadFromJsonAsync<JsonObject>();
        report.ShouldNotBeNull();
        report["verdict"]!.GetValue<string>().ShouldBeOneOf("ready", "warning", "blocked");
        var checks = report["checks"] as JsonArray;
        checks.ShouldNotBeNull();
        checks.ShouldNotBeEmpty();
        foreach (var item in checks)
        {
            var check = item as JsonObject;
            check.ShouldNotBeNull();
            check!["name"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
            check["status"].ShouldNotBeNull();
            check["message"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task Dado_DryRun_Quando_PostRuns_Entao_ResolvedSemPersistir()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var before = await client.GetFromJsonAsync<List<PipelineExecutionDto>>("/api/harness/runs");
        var request = new RunStartRequest(
            "quick-patch", "afonsoft/agent-harness", "main", "150", null,
            "Implementar JWT", DryRun: true);

        var response = await client.PostAsJsonAsync("/api/harness/runs", request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body.ShouldNotBeNull();
        body["resolved"].ShouldNotBeNull();
        body["resolved"]!["prompt"]!.GetValue<string>().ShouldContain("Implementar JWT");
        body["resolved"]!["templateId"]!.GetValue<string>().ShouldBe("quick-patch");
        body["verdict"]!.GetValue<string>().ShouldBeOneOf("ready", "warning", "blocked");
        (body["checks"] as JsonArray).ShouldNotBeNull();

        // Nada persistido: a lista de runs não cresceu.
        var after = await client.GetFromJsonAsync<List<PipelineExecutionDto>>("/api/harness/runs");
        after.ShouldNotBeNull();
        after.Count.ShouldBe(before!.Count);
    }
}
