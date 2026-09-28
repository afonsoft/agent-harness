using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260928-agent-cli-probe-background: the refresh status/trigger
/// endpoints are admin-only and report the background probe state.
/// </summary>
public class AgentCliRefreshEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AgentCliRefreshEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetRefresh_Entao_Retorna401()
    {
        var response = await _client.GetAsync("/api/agent-clis/refresh");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_PostRefresh_Entao_Retorna401()
    {
        var response = await _client.PostAsync("/api/agent-clis/refresh", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_GetRefresh_Entao_RetornaStatusComShape()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/agent-clis/refresh");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body.ShouldNotBeNull();
        body!["running"].ShouldNotBeNull();
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_PostRefresh_Entao_DisparaERetornaRunning()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync("/api/agent-clis/refresh", content: null);

        // 202 when a fresh refresh starts, 200 when one is already in flight —
        // both report running:true (single-flight).
        ((int)response.StatusCode).ShouldBeOneOf(200, 202);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body?["running"]?.GetValue<bool>().ShouldBeTrue();
    }
}
