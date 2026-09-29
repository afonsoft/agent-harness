using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260929-ai-code-provider-chat RF-001/RF-004/RF-005: CRUD de
/// providers (key mascarada), conversas e o caminho de erro do stream.
/// </summary>
public class ChatEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;

    public ChatEndpointsTests(TaskboardWebApplicationFactory factory) => _factory = factory;

    private Task<HttpClient> ApiClientAsync() => _factory.CreateAuthenticatedClientAsync();

    [Fact]
    public async Task Dado_ProviderValido_Quando_Criar_Entao_PersisteComKeyMascarada()
    {
        var client = await ApiClientAsync();

        var create = await client.PostAsJsonAsync("/api/local/chat/providers", new
        {
            name = "itest-provider",
            baseUrl = "http://localhost:59999",
            apiKey = "sk-secret-1234",
        });

        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await create.Content.ReadFromJsonAsync<JsonObject>();
        var provider = body!["provider"]!.AsObject();
        provider["name"]!.GetValue<string>().ShouldBe("itest-provider");
        provider["hasApiKey"]!.GetValue<bool>().ShouldBeTrue();
        provider["keyHint"]!.GetValue<string>().Contains("sk-secret-1234567890", StringComparison.Ordinal).ShouldBeFalse("a key nunca sai em claro (RF-001)");

        var list = await (await client.GetAsync("/api/local/chat/providers")).Content
            .ReadFromJsonAsync<JsonObject>();
        list!["providers"]!.AsArray().Any(p => p?["name"]?.GetValue<string>() == "itest-provider").ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_ProviderDuplicado_Quando_Criar_Entao_Retorna400()
    {
        var client = await ApiClientAsync();
        await client.PostAsJsonAsync("/api/local/chat/providers", new
        {
            name = "itest-dup",
            baseUrl = "http://localhost:59999",
            apiKey = "k",
        });

        var duplicate = await client.PostAsJsonAsync("/api/local/chat/providers", new
        {
            name = "itest-dup",
            baseUrl = "http://localhost:59998",
        });

        duplicate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_Conversa_Quando_CriarListarDetalharDeletar_Entao_CicloCompleto()
    {
        var client = await ApiClientAsync();
        var provider = await CreateProviderAsync(client);

        var create = await client.PostAsJsonAsync("/api/local/chat/conversations", new
        {
            providerId = provider["id"]!.GetValue<Guid>(),
            model = "m1",
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var conversation = (await create.Content.ReadFromJsonAsync<JsonObject>())!["conversation"]!.AsObject();
        var id = conversation["id"]!.GetValue<string>();

        var list = await (await client.GetAsync("/api/local/chat/conversations")).Content
            .ReadFromJsonAsync<JsonObject>();
        list!["conversations"]!.AsArray().Any(c => c?["id"]?.GetValue<string>() == id).ShouldBeTrue();

        var detail = await client.GetAsync($"/api/local/chat/conversations/{id}");
        detail.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detailBody = await detail.Content.ReadFromJsonAsync<JsonObject>();
        detailBody!["messages"]!.AsArray().ShouldBeEmpty();

        var delete = await client.DeleteAsync($"/api/local/chat/conversations/{id}");
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var after = await client.GetAsync($"/api/local/chat/conversations/{id}");
        after.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dado_ProviderInacessivel_Quando_EnviarMensagem_Entao_StreamComErroNoDone()
    {
        var client = await ApiClientAsync();
        var provider = await CreateProviderAsync(client);
        var create = await client.PostAsJsonAsync("/api/local/chat/conversations", new
        {
            providerId = provider["id"]!.GetValue<Guid>(),
            model = "m1",
        });
        var id = ((await create.Content.ReadFromJsonAsync<JsonObject>())!["conversation"] as JsonObject)!["id"]!
            .GetValue<string>();

        var response = await client.PostAsJsonAsync($"/api/local/chat/conversations/{id}/messages", new
        {
            content = "olá",
        });

        // O stream abre 200 e termina com chat.done carregando o erro do provider (RF-005).
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("event: chat.done");
        body.Contains("error", StringComparison.Ordinal).ShouldBeTrue("provider inacessível deve terminar o stream com erro no chat.done");
    }

    [Fact]
    public async Task Dado_ModelsEndpoint_Quando_ProviderForaDoAr_Entao_Retorna502()
    {
        var client = await ApiClientAsync();
        var provider = await CreateProviderAsync(client);

        var models = await client.GetAsync($"/api/local/chat/providers/{provider["id"]!.GetValue<Guid>()}/models");

        models.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
    }

    private static async Task<JsonObject> CreateProviderAsync(HttpClient client)
    {
        var create = await client.PostAsJsonAsync("/api/local/chat/providers", new
        {
            name = $"itest-{Guid.NewGuid():N}",
            baseUrl = "http://localhost:59999",
            apiKey = "sk-itest",
        });
        create.EnsureSuccessStatusCode();
        return (await create.Content.ReadFromJsonAsync<JsonObject>())!["provider"]!.AsObject();
    }
}
