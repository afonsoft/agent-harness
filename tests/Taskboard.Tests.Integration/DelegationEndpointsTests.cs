using System.Net;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20261005 RF-010: os endpoints de inspeção de delegação existem,
/// exigem auth e respondem sobre o schema da nova migration.
/// </summary>
public class DelegationEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public DelegationEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetTasks_Entao_401()
    {
        var response = await _client.GetAsync("/api/local/delegation/tasks");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetMailbox_Entao_401()
    {
        var response = await _client.GetAsync("/api/local/delegation/mailbox");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_GetTasks_Entao_200ComLista()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/local/delegation/tasks?scope=itest");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("tasks");
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_GetMailbox_Entao_200ComLista()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/local/delegation/mailbox?scope=itest");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("messages");
    }
}
