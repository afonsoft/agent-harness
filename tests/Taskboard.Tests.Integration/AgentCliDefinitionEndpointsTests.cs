using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Taskboard.Dtos;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260928-ai-code-generic-cli RF-002/RF-004: custom CLI CRUD,
/// docker container listing and the installed-agents endpoint.
/// </summary>
public class AgentCliDefinitionEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AgentCliDefinitionEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetCustom_Entao_Retorna401()
    {
        var response = await _client.GetAsync("/api/agents/custom");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_RequestValida_Quando_PostCustom_Entao_201EIdCustom()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var name = $"itest-{Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync("/api/agents/custom",
            new UpsertAgentCliDefinitionRequest(
                name, "definitely-not-on-path-cli", "--fast {model}", "pty"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var def = await response.Content.ReadFromJsonAsync<AgentCliDefinitionDto>();
        def.ShouldNotBeNull();
        def!.Id.ShouldStartWith("custom-");
        def.DisplayName.ShouldBe(name);
        def.Transport.ShouldBe("pty");
        def.Resolved.ShouldBeFalse(); // binary does not exist on the host

        // Visible in the list.
        var list = await client.GetFromJsonAsync<List<AgentCliDefinitionDto>>("/api/agents/custom");
        list.ShouldNotBeNull();
        list.ShouldContain(d => d.Id == def.Id);
    }

    [Fact]
    public async Task Dado_DisplayNameDuplicado_Quando_PostCustom_Entao_409()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var name = $"dup-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/agents/custom",
            new UpsertAgentCliDefinitionRequest(name, "cli-a", null, "pty"));

        var response = await client.PostAsJsonAsync("/api/agents/custom",
            new UpsertAgentCliDefinitionRequest(name, "cli-b", null, "pty"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Dado_TransporteInvalido_Quando_PostCustom_Entao_400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/agents/custom",
            new UpsertAgentCliDefinitionRequest("x" + Guid.NewGuid().ToString("N")[..8],
                "cli", null, "websocket"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_ExecutavelVazio_Quando_PostCustom_Entao_400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/agents/custom",
            new UpsertAgentCliDefinitionRequest("y" + Guid.NewGuid().ToString("N")[..8],
                "   ", null, "pty"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_DefExistente_Quando_PutEDelete_Entao_AtualizaERemove()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/agents/custom",
            new UpsertAgentCliDefinitionRequest("z" + Guid.NewGuid().ToString("N")[..8],
                "cli-1", "--a", "pty"));
        var def = await created.Content.ReadFromJsonAsync<AgentCliDefinitionDto>();
        def.ShouldNotBeNull();

        var put = await client.PutAsJsonAsync($"/api/agents/custom/{def!.Id}",
            new UpsertAgentCliDefinitionRequest(def.DisplayName, "cli-2", "--b", "pty",
                Enabled: false));
        put.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await put.Content.ReadFromJsonAsync<AgentCliDefinitionDto>();
        updated!.Executable.ShouldBe("cli-2");
        updated.Enabled.ShouldBeFalse();

        var del = await client.DeleteAsync($"/api/agents/custom/{def.Id}");
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var del2 = await client.DeleteAsync($"/api/agents/custom/{def.Id}");
        del2.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dado_DefInexistente_Quando_Put_Entao_404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync("/api/agents/custom/custom-nope",
            new UpsertAgentCliDefinitionRequest("nope", "cli", null, "pty"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dado_SemDockerDaemon_Quando_GetContainers_Entao_503()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/agents/docker/containers");

        // The factory's docker locator never resolves "docker" — daemon unavailable.
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Dado_Builtins_Quando_GetInstalled_Entao_ListaComTransport()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/agents/installed");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body.ShouldNotBeNull();
        var agents = body!["agents"]!.AsArray();
        agents.ShouldNotBeEmpty();
        // FakeAgentDiscoveryService reports every AgentType — entries carry
        // the native transport (acp for ACP-capable, pty otherwise).
        agents.Any(a => a?["transport"]?.GetValue<string>() == "pty").ShouldBeTrue();
        agents.Any(a => a?["transport"]?.GetValue<string>() == "acp").ShouldBeTrue();
    }
}
