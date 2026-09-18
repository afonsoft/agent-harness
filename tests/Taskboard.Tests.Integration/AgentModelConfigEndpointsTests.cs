using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260918-agent-model-config: per-CLI model tier endpoints
/// (GET/PUT/DELETE /api/agents/{type}/models).
/// </summary>
public class AgentModelConfigEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AgentModelConfigEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetModels_Entao_Retorna401()
    {
        var response = await _client.GetAsync("/api/agents/Claude/models");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_CliSuportada_Quando_GetModels_Entao_DefaultsCurados()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/agents/Claude/models");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body?["supportsModelSelection"]?.GetValue<bool>().ShouldBeTrue();
        body?["source"]?.GetValue<string>().ShouldBe("default");
        body?["lite"]?.GetValue<string>().ShouldBe("haiku");
        body?["normal"]?.GetValue<string>().ShouldBe("sonnet");
        body?["ultra"]?.GetValue<string>().ShouldBe("opus");
        body?["catalog"]?.AsArray().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Dado_CliGerenciada_Quando_GetModels_Entao_422()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/agents/Cline/models");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body?["error"]?.GetValue<string>().ShouldBe("model-selection-unsupported");
    }

    [Fact]
    public async Task Dado_OverrideSalvo_Quando_GetEDelete_Entao_EffectiveEDefault()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var putResponse = await client.PutAsJsonAsync(
            "/api/agents/Devin/models", new { lite = "custom-lite", ultra = "custom-ultra" });
        putResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var putBody = await putResponse.Content.ReadFromJsonAsync<JsonObject>();
        putBody?["source"]?.GetValue<string>().ShouldBe("override");
        putBody?["lite"]?.GetValue<string>().ShouldBe("custom-lite");
        putBody?["normal"]?.GetValue<string>().ShouldBe("swe"); // slot ausente → curado
        putBody?["ultra"]?.GetValue<string>().ShouldBe("custom-ultra");

        var deleteResponse = await client.DeleteAsync("/api/agents/Devin/models");
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var deleteBody = await deleteResponse.Content.ReadFromJsonAsync<JsonObject>();
        deleteBody?["source"]?.GetValue<string>().ShouldBe("default");
        deleteBody?["lite"]?.GetValue<string>().ShouldBe("haiku");
    }

    [Fact]
    public async Task Dado_CliSemProbe_Quando_GetAvailable_Entao_200ListaVazia()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // Claude supports --model but has no headless model-list command;
        // on CI no CLI is installed anyway, so the list is empty either way.
        var response = await client.GetAsync("/api/agents/Claude/models/available");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body?["models"]?.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_CliGerenciada_Quando_GetAvailable_Entao_422()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/agents/Cline/models/available");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body?["error"]?.GetValue<string>().ShouldBe("model-selection-unsupported");
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetAvailable_Entao_401()
    {
        var response = await _client.GetAsync("/api/agents/OpenCode/models/available");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_ModeloMuitoLongo_Quando_Put_Entao_400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/agents/Claude/models", new { normal = new string('x', 200) });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body?["error"]?.GetValue<string>().ShouldBe("invalid-model");
    }
}
