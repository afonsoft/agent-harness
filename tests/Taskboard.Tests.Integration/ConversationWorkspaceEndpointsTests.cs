using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20261011-chat-workspace-panel RF-003/RF-004/RF-005/RF-006/RF-008:
/// endpoints workspace/todos/diff/plan por conversa + o open do PTY
/// conversation-scoped (conv-&lt;id&gt;) no /terminal-hub.
/// </summary>
public class ConversationWorkspaceEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;

    public ConversationWorkspaceEndpointsTests(TaskboardWebApplicationFactory factory) => _factory = factory;

    private Task<HttpClient> ApiClientAsync() => _factory.CreateAuthenticatedClientAsync();

    [Fact]
    public async Task Dado_ConversaInexistente_Quando_WorkspaceTodosDiffPlan_Entao_404()
    {
        var client = await ApiClientAsync();

        foreach (var suffix in new[] { "workspace", "todos", "diff", "plan" })
        {
            var response = await client.GetAsync($"/api/local/chat/conversations/nao-existe/{suffix}");
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"endpoint {suffix}");
        }
    }

    [Fact]
    public async Task Dado_ConversaCriada_Quando_Workspace_Entao_200ComShape()
    {
        var client = await ApiClientAsync();
        var id = await CreateConversationAsync(client);

        var response = await client.GetAsync($"/api/local/chat/conversations/{id}/workspace");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        var workspace = body!["workspace"]!.AsObject();
        workspace.ContainsKey("path").ShouldBeTrue();
        workspace.ContainsKey("isGit").ShouldBeTrue();
        workspace.ContainsKey("dirty").ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_ConversaCriada_Quando_Todos_Entao_200ListaVazia()
    {
        var client = await ApiClientAsync();
        var id = await CreateConversationAsync(client);

        var response = await client.GetAsync($"/api/local/chat/conversations/{id}/todos");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body!["todos"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_ConversaCriada_Quando_Diff_Entao_200ComShape()
    {
        var client = await ApiClientAsync();
        var id = await CreateConversationAsync(client);

        var response = await client.GetAsync($"/api/local/chat/conversations/{id}/diff");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        // serializer omite nulls — sem chave diff = sem mudanças; com chave,
        // vem o WorkspaceDiffDto completo.
        if (body!.ContainsKey("diff"))
        {
            body["diff"]!.AsObject().ContainsKey("files").ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Dado_ConversaCriada_Quando_Plan_Entao_200PlanNull()
    {
        var client = await ApiClientAsync();
        var id = await CreateConversationAsync(client);

        var response = await client.GetAsync($"/api/local/chat/conversations/{id}/plan");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        // serializer omite nulls — sem chave plan = nenhum plan-review ainda.
        if (body!.ContainsKey("plan"))
        {
            body["plan"]!.AsObject().ContainsKey("status").ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Dado_ConversaCriada_Quando_OpenForConversation_Entao_SessaoConv()
    {
        // RF-006: o hub resolve o workdir da conversa e devolve a sessão
        // determinística conv-<id> — PTY vivo sobrevive a troca de aba.
        var client = await ApiClientAsync();
        var id = await CreateConversationAsync(client);

        await using var hub = new HubConnectionBuilder()
            .WithUrl("http://localhost/terminal-hub", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Headers["X-Api-Key"] = TaskboardWebApplicationFactory.TestApiKey;
            })
            .Build();
        await hub.StartAsync();
        hub.State.ShouldBe(HubConnectionState.Connected);

        var sessionId = await hub.InvokeAsync<string>("OpenForConversation", id);

        sessionId.ShouldBe($"conv-{id}");
        await hub.InvokeAsync("Close", sessionId);
    }

    private async Task<string> CreateConversationAsync(HttpClient client)
    {
        var provider = await client.PostAsJsonAsync("/api/local/chat/providers", new
        {
            name = $"itest-ws-{Guid.NewGuid():N}",
            baseUrl = "http://localhost:59999",
            apiKey = "sk-itest",
        });
        provider.EnsureSuccessStatusCode();
        var providerId = ((await provider.Content.ReadFromJsonAsync<JsonObject>())!["provider"] as JsonObject)!["id"]!
            .GetValue<Guid>();

        var create = await client.PostAsJsonAsync("/api/local/chat/conversations", new
        {
            providerId,
            model = "m1",
        });
        create.EnsureSuccessStatusCode();
        return ((await create.Content.ReadFromJsonAsync<JsonObject>())!["conversation"] as JsonObject)!["id"]!
            .GetValue<string>();
    }
}
