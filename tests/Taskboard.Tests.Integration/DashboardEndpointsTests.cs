using System.Net;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20261006: endpoints do agent dashboard + session history exigem auth
/// e respondem 200 autenticados.
/// </summary>
public class DashboardEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public DashboardEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetDashboard_Entao_401()
    {
        var response = await _client.GetAsync("/api/local/delegation/dashboard");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_GetDashboard_Entao_200ComColunas()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/local/delegation/dashboard?scope=itest");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("needsYou");
        body.ShouldContain("working");
        body.ShouldContain("done");
        body.ShouldContain("idle");
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetSessions_Entao_401()
    {
        var response = await _client.GetAsync("/api/agents/sessions");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_GetSessions_Entao_200ComLista()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/agents/sessions");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("sessions");
    }
}
