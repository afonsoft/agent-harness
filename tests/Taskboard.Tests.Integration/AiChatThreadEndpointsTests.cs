using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

public class AiChatThreadEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AiChatThreadEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private Task<HttpClient> ApiClientAsync() => _factory.CreateAuthenticatedClientAsync();

    [Fact]
    public async Task Dado_SemCredenciais_Quando_ListThreads_Entao_Retorna401()
    {
        var response = await _client.GetAsync("/api/local/ai/threads");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_DeleteThread_Entao_Retorna401()
    {
        var response = await _client.DeleteAsync("/api/local/ai/threads/abc");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_RequestValida_Quando_CreateThread_Entao_Retorna201EApareceNaLista()
    {
        var client = await ApiClientAsync();

        var create = await client.PostAsJsonAsync("/api/local/ai/threads", new
        {
            title = "Integration thread",
            model = "opencode/claude-sonnet-5",
            reasoningEffort = "medium",
            sandbox = "read-only",
            agentType = "OpenCode"
        });

        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<JsonObject>();
        var thread = created?["thread"] as JsonObject;
        thread.ShouldNotBeNull();
        var threadId = thread!["id"]!.GetValue<string>();
        thread["title"]!.GetValue<string>().ShouldBe("Integration thread");
        thread["agentType"]!.GetValue<string>().ShouldBe("OpenCode");

        var list = await client.GetFromJsonAsync<JsonObject>("/api/local/ai/threads");
        var threads = list?["threads"] as JsonArray;
        threads.ShouldNotBeNull();
        threads!.Any(t => t?["id"]?.GetValue<string>() == threadId).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_ThreadCriada_Quando_Delete_Entao_Retorna204ENaoListaMais()
    {
        var client = await ApiClientAsync();
        var threadId = await CreateThreadAsync(client, "delete me");

        var delete = await client.DeleteAsync($"/api/local/ai/threads/{threadId}");

        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var list = await client.GetFromJsonAsync<JsonObject>("/api/local/ai/threads");
        var threads = list?["threads"] as JsonArray;
        threads.ShouldNotBeNull();
        threads!.Any(t => t?["id"]?.GetValue<string>() == threadId).ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_ThreadInexistente_Quando_Delete_Entao_Retorna404()
    {
        var client = await ApiClientAsync();

        var response = await client.DeleteAsync("/api/local/ai/threads/does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dado_ThreadCriada_Quando_PostEvent_Quando_GetEventsComAcceptJson_Entao_RetornaSnapshotJson()
    {
        var client = await ApiClientAsync();
        var threadId = await CreateThreadAsync(client, "events thread");

        var post = await client.PostAsJsonAsync($"/api/local/ai/threads/{threadId}/events", new
        {
            role = "user",
            content = "primeira mensagem"
        });
        post.StatusCode.ShouldBe(HttpStatusCode.Created);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/local/ai/threads/{threadId}/events");
        request.Headers.Accept.ParseAdd("application/json");
        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        var events = body?["events"] as JsonArray;
        events.ShouldNotBeNull();
        events!.Count.ShouldBe(1);
        events[0]!["content"]!.GetValue<string>().ShouldBe("primeira mensagem");
    }

    [Fact]
    public async Task Dado_EventoDoUsuario_Quando_StartRun_Entao_AssistenteRespondeAMensagemReal()
    {
        // AC4: o run deve responder à última mensagem real do usuário —
        // a injeção fixa de "Continue the conversation." foi removida.
        var client = await ApiClientAsync();
        var threadId = await CreateThreadAsync(client, "run thread");
        const string userMessage = "pergunta-marcador-xyz";

        (await client.PostAsJsonAsync($"/api/local/ai/threads/{threadId}/events", new
        {
            role = "user",
            content = userMessage
        })).EnsureSuccessStatusCode();

        var run = await client.PostAsync($"/api/local/ai/threads/{threadId}/runs", content: null);
        run.StatusCode.ShouldBe(HttpStatusCode.Created);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        JsonArray? events = null;
        while (DateTime.UtcNow < deadline)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/local/ai/threads/{threadId}/events");
            request.Headers.Accept.ParseAdd("application/json");
            var body = await (await client.SendAsync(request)).Content.ReadFromJsonAsync<JsonObject>();
            events = body?["events"] as JsonArray;
            if (events?.Any(e => e?["role"]?.GetValue<string>() == "assistant") == true)
            {
                break;
            }

            await Task.Delay(250);
        }

        events.ShouldNotBeNull();
        var assistantContent = string.Concat(
            events!
                .Where(e => e?["role"]?.GetValue<string>() == "assistant")
                .Select(e => e!["content"]!.GetValue<string>()));

        // MockLLMProvider echoes the last user message — proves the run
        // consumed the real history instead of the fixed injected prompt.
        assistantContent.ShouldContain(userMessage);
        assistantContent.ShouldNotContain("Continue the conversation.");
    }

    [Fact]
    public async Task Dado_RequestModoAgent_Quando_CreateThread_Entao_Retorna201ComModeEAgentType()
    {
        // Covers RF-001: Thread em modo agent
        var client = await ApiClientAsync();

        var create = await client.PostAsJsonAsync("/api/local/ai/threads", new
        {
            title = "Agent Thread Test",
            mode = "agent",
            agentType = "OpenCode",
            workspacePath = "/tmp/test-workspace",
            sandbox = "workspace-write"
        });

        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<JsonObject>();
        var thread = created?["thread"] as JsonObject;
        thread.ShouldNotBeNull();
        thread!["mode"]!.GetValue<string>().ShouldBe("agent");
        thread["agentType"]!.GetValue<string>().ShouldBe("OpenCode");
        thread["workspacePath"]!.GetValue<string>().ShouldBe("/tmp/test-workspace");
    }

    [Fact]
    public async Task Dado_FeatureFlagOff_Quando_PostPrompt_Entao_Retorna404()
    {
        // Covers RF-009: Feature flag desabilitada
        var client = await ApiClientAsync();
        var threadId = await CreateThreadAsync(client, "Thread flag off");

        var response = await client.PostAsJsonAsync($"/api/local/ai/threads/{threadId}/prompt", new
        {
            text = "Hello agent"
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dado_SemAgentType_Quando_CreateThread_Entao_Retorna400()
    {
        // SPEC-20260921-ai-chat-cli-backend RF-002: toda thread exige um agente.
        var client = await ApiClientAsync();

        var create = await client.PostAsJsonAsync("/api/local/ai/threads", new
        {
            title = "no agent",
            sandbox = "read-only"
        });

        create.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<string> CreateThreadAsync(HttpClient client, string title)
    {
        var create = await client.PostAsJsonAsync("/api/local/ai/threads", new
        {
            title,
            model = "opencode/claude-sonnet-5",
            reasoningEffort = "medium",
            sandbox = "read-only",
            agentType = "OpenCode"
        });
        create.EnsureSuccessStatusCode();
        var body = await create.Content.ReadFromJsonAsync<JsonObject>();
        return body!["thread"]!["id"]!.GetValue<string>();
    }
}
