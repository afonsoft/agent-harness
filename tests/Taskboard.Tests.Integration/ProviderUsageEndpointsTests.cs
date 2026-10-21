using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20261020-provider-quota-usage P1: /api/usage surface — roster, detail
/// 404, manual refresh. The claude CLI collector failing in the test env (no
/// binary/auth) exercises RF-009 failure tolerance: refresh still 200s with
/// upserted=0 and the job never dies.
/// </summary>
public class ProviderUsageEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ProviderUsageEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetProviders_Entao_Retorna401()
    {
        var response = await _client.GetAsync("/api/usage/providers");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_GetProviders_Entao_RetornaRoster()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/usage/providers");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonArray>();
        body.ShouldNotBeNull(); // may be empty — claude CLI absent in test env.
    }

    [Fact]
    public async Task Dado_ProviderDesconhecido_Quando_GetDetail_Entao_Retorna404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/usage/providers/nope");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dado_CollectorIndisponivel_Quando_Refresh_Entao_Tolerante()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync("/api/usage/refresh", null);

        // Collector failure must not kill the endpoint — 200 + upserted count.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body.ShouldNotBeNull();
        body!["upserted"].ShouldNotBeNull();

        // Roster still serves afterwards (job/service not poisoned).
        var roster = await client.GetAsync("/api/usage/providers");
        roster.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
