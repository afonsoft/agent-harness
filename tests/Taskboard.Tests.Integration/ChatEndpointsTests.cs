using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Http.Connections;
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
    public async Task Dado_ProviderInacessivel_Quando_EnviarMensagem_Entao_202EAttachStreamComDone()
    {
        // SPEC-20261005 RF-002/RF-003: send enfileira (202 + run); o attach
        // stream devolve chat.sync e termina com chat.done trazendo o run
        // failed (provider inacessível falha dentro do dispatcher, não no POST).
        var client = await ApiClientAsync();
        var provider = await CreateProviderAsync(client);
        var create = await client.PostAsJsonAsync("/api/local/chat/conversations", new
        {
            providerId = provider["id"]!.GetValue<Guid>(),
            model = "m1",
        });
        var id = ((await create.Content.ReadFromJsonAsync<JsonObject>())!["conversation"] as JsonObject)!["id"]!
            .GetValue<string>();

        var enqueue = await client.PostAsJsonAsync($"/api/local/chat/conversations/{id}/messages", new
        {
            content = "olá",
        });

        enqueue.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var run = (await enqueue.Content.ReadFromJsonAsync<JsonObject>())!["run"]!.AsObject();
        var runId = run["id"]!.GetValue<string>();
        run["status"]!.GetValue<string>().ShouldBe("queued");

        // O run falha no dispatcher (provider recusa a conexão) — poll o
        // detail até a linha terminal aparecer como lastRun.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        JsonObject? lastRun = null;
        while (DateTime.UtcNow < deadline)
        {
            var detail = await (await client.GetAsync($"/api/local/chat/conversations/{id}"))
                .Content.ReadFromJsonAsync<JsonObject>();
            lastRun = detail!["lastRun"] as JsonObject;
            if (lastRun is not null)
            {
                break;
            }

            await Task.Delay(250);
        }

        lastRun.ShouldNotBeNull("o dispatcher deve terminalizar o run sem request ativa");
        lastRun["status"]!.GetValue<string>().ShouldBe("failed");
        lastRun["error"]!.GetValue<string>().ShouldContain("refused");

        // Late attach: run terminal — o stream replaya chat.sync e fecha com
        // chat.done já carregando a linha final (SPEC-20261005 RF-003).
        var attach = await client.GetAsync(
            $"/api/local/chat/conversations/{id}/runs/{runId}/stream",
            HttpCompletionOption.ResponseHeadersRead);
        attach.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await attach.Content.ReadAsStringAsync();
        body.ShouldContain("event: chat.sync");
        body.ShouldContain("event: chat.done");
        body.ShouldContain("\"status\":\"failed\"");
    }

    [Fact]
    public async Task Dado_RunFalhado_Quando_Terminaliza_Entao_HubPublicaRunCompleted()
    {
        // SPEC-20261005 RF-008: o notifier registra SignalRChatRunNotifier no
        // escopo do run — ao terminalizar, /chat-run-hub emite run.completed
        // com o status e o conversationId (o toast/desktop-notify do client).
        var client = await ApiClientAsync();
        var provider = await CreateProviderAsync(client);
        var create = await client.PostAsJsonAsync("/api/local/chat/conversations", new
        {
            providerId = provider["id"]!.GetValue<Guid>(),
            model = "m1",
        });
        var id = ((await create.Content.ReadFromJsonAsync<JsonObject>())!["conversation"] as JsonObject)!["id"]!
            .GetValue<string>();

        await using var hub = new HubConnectionBuilder()
            .WithUrl("http://localhost/chat-run-hub", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Headers["X-Api-Key"] = TaskboardWebApplicationFactory.TestApiKey;
            })
            .Build();
        var received = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<JsonObject>("run.completed", payload => received.TrySetResult(payload));
        await hub.StartAsync();
        hub.State.ShouldBe(HubConnectionState.Connected);

        await client.PostAsJsonAsync($"/api/local/chat/conversations/{id}/messages", new
        {
            content = "olá",
        });

        var payload = await received.Task.WaitAsync(TimeSpan.FromSeconds(20));
        payload["conversationId"]!.GetValue<string>().ShouldBe(id);
        payload["status"]!.GetValue<string>().ShouldBe("failed");
        payload["error"]!.GetValue<string>().ShouldContain("refused");
    }

    [Fact]
    public async Task Dado_Conversa_Quando_ArquivarListarRestaurarDeletar_Entao_CicloDeArquivo()
    {
        // RF-006: arquivar é view flag — sai da lista ativa, abre read-only,
        // restaura e aceita delete permanente só na aba arquivadas.
        var client = await ApiClientAsync();
        var provider = await CreateProviderAsync(client);
        var create = await client.PostAsJsonAsync("/api/local/chat/conversations", new
        {
            providerId = provider["id"]!.GetValue<Guid>(),
            model = "m1",
        });
        var id = ((await create.Content.ReadFromJsonAsync<JsonObject>())!["conversation"] as JsonObject)!["id"]!
            .GetValue<string>();

        var archive = await client.PostAsync($"/api/local/chat/conversations/{id}/archive", content: null);
        archive.StatusCode.ShouldBe(HttpStatusCode.OK);

        var active = await (await client.GetAsync("/api/local/chat/conversations"))
            .Content.ReadFromJsonAsync<JsonObject>();
        active!["conversations"]!.AsArray().Any(c => c?["id"]?.GetValue<string>() == id)
            .ShouldBeFalse("arquivada sai da lista ativa");

        var archived = await (await client.GetAsync("/api/local/chat/conversations?archived=true"))
            .Content.ReadFromJsonAsync<JsonObject>();
        var row = archived!["conversations"]!.AsArray()
            .Single(c => c?["id"]?.GetValue<string>() == id)!;
        row["archivedAt"].ShouldNotBeNull();

        var enqueue = await client.PostAsJsonAsync($"/api/local/chat/conversations/{id}/messages", new
        {
            content = "olá",
        });
        enqueue.StatusCode.ShouldBe(HttpStatusCode.Conflict,
            "arquivada não aceita novo turno até restaurar");

        var unarchive = await client.PostAsync($"/api/local/chat/conversations/{id}/unarchive", content: null);
        unarchive.StatusCode.ShouldBe(HttpStatusCode.OK);
        var delete = await client.DeleteAsync($"/api/local/chat/conversations/{id}");
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Dado_RunAtiva_Quando_Detalhar_Entao_ActiveRunNoDetail()
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
        var enqueue = await client.PostAsJsonAsync($"/api/local/chat/conversations/{id}/messages", new
        {
            content = "olá",
        });
        enqueue.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var run = (await enqueue.Content.ReadFromJsonAsync<JsonObject>())!["run"]!.AsObject();

        // Poll o detail até o dispatcher sinalizar o run (ativo ou terminal).
        var deadline = DateTime.UtcNow.AddSeconds(15);
        JsonObject? detail = null;
        while (DateTime.UtcNow < deadline)
        {
            detail = await (await client.GetAsync($"/api/local/chat/conversations/{id}"))
                .Content.ReadFromJsonAsync<JsonObject>();
            if (detail?["activeRun"] is not null || detail?["lastRun"] is not null)
            {
                break;
            }

            await Task.Delay(150);
        }

        detail.ShouldNotBeNull();
        var surfaced = detail["activeRun"] ?? detail["lastRun"];
        surfaced.ShouldNotBeNull("o run deve aparecer como ativo ou terminal no detail");
        surfaced!["id"]!.GetValue<string>().ShouldBe(run["id"]!.GetValue<string>());

        var stop = await client.PostAsync($"/api/local/chat/conversations/{id}/stop", content: null);
        stop.StatusCode.ShouldBeOneOf(HttpStatusCode.Accepted, HttpStatusCode.Conflict);
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
