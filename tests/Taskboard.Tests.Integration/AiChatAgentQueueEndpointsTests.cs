using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260921-ai-code-chat-ux RF-002 (fila FIFO) e RF-004 (fork/retry).
/// A factory habilita Taskboard:WebCliAgent; o spawn real falha (binário fake),
/// então o dispatch fica queued — deterministicamente testável.
/// </summary>
public class AiChatAgentQueueEndpointsTests : IClassFixture<AiChatAgentQueueEndpointsTests.AgentModeFactory>
{
    public sealed class AgentModeFactory : TaskboardWebApplicationFactory
    {
        public AgentModeFactory()
        {
            WebCliAgentEnabled = true;
        }
    }

    private readonly AgentModeFactory _factory;

    public AiChatAgentQueueEndpointsTests(AgentModeFactory factory)
    {
        _factory = factory;
    }

    private Task<HttpClient> ApiClientAsync() => _factory.CreateAuthenticatedClientAsync();

    private static async Task<string> CreateAgentThreadAsync(HttpClient client, string title)
    {
        var create = await client.PostAsJsonAsync("/api/local/ai/threads", new
        {
            title,
            mode = "agent",
            agentType = "OpenCode",
            workspacePath = "/tmp/itest-ws",
            sandbox = "workspace-write"
        });
        create.EnsureSuccessStatusCode();
        var body = await create.Content.ReadFromJsonAsync<JsonObject>();
        return body!["thread"]!["id"]!.GetValue<string>();
    }

    private static async Task<JsonArray> GetEventsAsync(HttpClient client, string threadId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/local/ai/threads/{threadId}/events");
        request.Headers.Accept.ParseAdd("application/json");
        var body = await (await client.SendAsync(request)).Content.ReadFromJsonAsync<JsonObject>();
        return body!["events"]!.AsArray();
    }

    [Fact]
    public async Task Dado_ThreadAgent_Quando_QueuePrompt_Entao_PersisteEventoQueued()
    {
        // RF-002: prompt vira evento "queued" persistido (spawn falha nos
        // testes → dispatch não consome).
        var client = await ApiClientAsync();
        var threadId = await CreateAgentThreadAsync(client, "queue thread");

        var response = await client.PostAsJsonAsync($"/api/local/ai/threads/{threadId}/queue", new
        {
            text = "primeiro prompt enfileirado"
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        var evt = body!["aiChatEvent"] as JsonObject;
        evt.ShouldNotBeNull();
        evt!["role"]!.GetValue<string>().ShouldBe("queued");
        evt["content"]!.GetValue<string>().ShouldBe("primeiro prompt enfileirado");

        var events = await GetEventsAsync(client, threadId);
        events.Any(e => e?["role"]?.GetValue<string>() == "queued").ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_PromptEnfileirado_Quando_Cancela_Entao_RemoveEvento()
    {
        // RF-002: cancel antes do dispatch remove o evento.
        var client = await ApiClientAsync();
        var threadId = await CreateAgentThreadAsync(client, "cancel queue");

        var queued = await client.PostAsJsonAsync($"/api/local/ai/threads/{threadId}/queue", new
        {
            text = "cancelar isto"
        });
        var eventId = ((await queued.Content.ReadFromJsonAsync<JsonObject>())!["aiChatEvent"] as JsonObject)!["id"]!
            .GetValue<string>();

        var cancel = await client.DeleteAsync($"/api/local/ai/threads/{threadId}/queue/{eventId}");
        cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var events = await GetEventsAsync(client, threadId);
        events.Any(e => e?["id"]?.GetValue<string>() == eventId).ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_ThreadAssistant_Quando_QueuePrompt_Entao_Retorna409()
    {
        // RF-002: fila só existe para threads em modo agent.
        var client = await ApiClientAsync();
        var create = await client.PostAsJsonAsync("/api/local/ai/threads", new
        {
            title = "assistant thread",
            agentType = "OpenCode",
            sandbox = "read-only"
        });
        create.EnsureSuccessStatusCode();
        var threadId = ((await create.Content.ReadFromJsonAsync<JsonObject>())!["thread"] as JsonObject)!["id"]!
            .GetValue<string>();

        var response = await client.PostAsJsonAsync($"/api/local/ai/threads/{threadId}/queue", new
        {
            text = "não deve enfileirar"
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Dado_ThreadComEventos_Quando_ForkNoPrimeiro_Entao_CopiaAteOEvento()
    {
        // RF-004: fork copia os eventos até o ponto selecionado e marca o título.
        var client = await ApiClientAsync();
        var threadId = await CreateAgentThreadAsync(client, "fork source");

        var e1 = await client.PostAsJsonAsync($"/api/local/ai/threads/{threadId}/events", new
        {
            role = "user",
            content = "mensagem um"
        });
        var e1Id = ((await e1.Content.ReadFromJsonAsync<JsonObject>())!["aiChatEvent"] as JsonObject)!["id"]!
            .GetValue<string>();
        (await client.PostAsJsonAsync($"/api/local/ai/threads/{threadId}/events", new
        {
            role = "user",
            content = "mensagem dois"
        })).EnsureSuccessStatusCode();

        var fork = await client.PostAsJsonAsync($"/api/local/ai/threads/{threadId}/fork", new
        {
            eventId = e1Id
        });

        fork.StatusCode.ShouldBe(HttpStatusCode.Created);
        var forkThread = (await fork.Content.ReadFromJsonAsync<JsonObject>())!["thread"] as JsonObject;
        forkThread.ShouldNotBeNull();
        forkThread!["title"]!.GetValue<string>().ShouldContain("source: fork");
        forkThread["mode"]!.GetValue<string>().ShouldBe("agent");

        var forkId = forkThread["id"]!.GetValue<string>();
        var forkEvents = await GetEventsAsync(client, forkId);
        forkEvents.Count.ShouldBe(1);
        forkEvents[0]!["content"]!.GetValue<string>().ShouldBe("mensagem um");
    }

    [Fact]
    public async Task Dado_EventoInexistente_Quando_Fork_Entao_Retorna400()
    {
        var client = await ApiClientAsync();
        var threadId = await CreateAgentThreadAsync(client, "fork bad event");

        var fork = await client.PostAsJsonAsync($"/api/local/ai/threads/{threadId}/fork", new
        {
            eventId = "evt-inexistente"
        });

        fork.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_SemPromptUsuario_Quando_Retry_Entao_Retorna409()
    {
        // RF-004: retry sem prompt anterior → conflito.
        var client = await ApiClientAsync();
        var threadId = await CreateAgentThreadAsync(client, "retry empty");

        var response = await client.PostAsync($"/api/local/ai/threads/{threadId}/retry", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Dado_PromptUsuario_Quando_RetrySemSessao_Entao_Retorna409PoisSpawnFalha()
    {
        // RF-004: com prompt anterior, retry tenta reenviar — nos testes o
        // spawn do agente falha (binário fake), então 409.
        var client = await ApiClientAsync();
        var threadId = await CreateAgentThreadAsync(client, "retry prompt");
        (await client.PostAsJsonAsync($"/api/local/ai/threads/{threadId}/events", new
        {
            role = "user",
            content = "prompt original"
        })).EnsureSuccessStatusCode();

        var response = await client.PostAsync($"/api/local/ai/threads/{threadId}/retry", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
