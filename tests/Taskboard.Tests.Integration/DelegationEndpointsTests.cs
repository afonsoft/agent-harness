using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Dtos;
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

    // ---- SPEC-20261007 RF-001: reply/dismiss ----

    [Fact]
    public async Task Dado_SemCredenciais_Quando_ReplyMailbox_Entao_401()
    {
        var response = await _client.PostAsync(
            "/api/local/delegation/mailbox/msg-x/reply",
            new StringContent("{\"body\":\"oi\"}", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_ReplyMsgInexistente_Entao_404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(
            "/api/local/delegation/mailbox/msg-x/reply?scope=itest",
            new StringContent("{\"body\":\"oi\"}", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldContain("mailbox-message-not-found");
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_DismissMsgInexistente_Entao_404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(
            "/api/local/delegation/mailbox/msg-x/dismiss?scope=itest", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dado_MsgNaCaixa_Quando_Reply_Entao_200EMarcaOriginalComoLida()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var scope = $"itest-reply-{Guid.NewGuid():N}";
        await using var diScope = _factory.Services.GetService<IServiceScopeFactory>()!
            .CreateAsyncScope();
        var delegation = diScope.ServiceProvider.GetRequiredService<IDelegationService>();
        var original = await delegation.PostAsync(
            new PostMailboxMessageRequest(scope, "codex", "@all", "need input", "escalation"));

        var response = await client.PostAsync(
            $"/api/local/delegation/mailbox/{original.Id}/reply?scope={scope}",
            new StringContent("{\"body\":\"do X first\"}", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("reply");

        // Fresh scope — the seeding scope's DbContext still tracks the stale entity.
        await using var readScope = _factory.Services.GetService<IServiceScopeFactory>()!
            .CreateAsyncScope();
        var reader = readScope.ServiceProvider.GetRequiredService<IDelegationService>();
        var updated = await reader.ReadInboxAsync(
            scope, [original.ToAgent], unreadOnly: false, markRead: false);
        updated.Single(m => m.Id == original.Id).ReadAt.ShouldNotBeNull();
        var inbox = await reader.ReadInboxAsync(
            scope, [original.FromAgent], unreadOnly: false, markRead: false);
        inbox.ShouldContain(m => m.Payload.Contains("do X first") && m.FromAgent == "human");
    }

    [Fact]
    public async Task Dado_MsgNaCaixa_Quando_Dismiss_Entao_204EMarcaComoLida()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var scope = $"itest-dismiss-{Guid.NewGuid():N}";
        await using var diScope = _factory.Services.GetService<IServiceScopeFactory>()!
            .CreateAsyncScope();
        var delegation = diScope.ServiceProvider.GetRequiredService<IDelegationService>();
        var original = await delegation.PostAsync(
            new PostMailboxMessageRequest(scope, "codex", "@all", "stale", "heartbeat"));

        var response = await client.PostAsync(
            $"/api/local/delegation/mailbox/{original.Id}/dismiss?scope={scope}", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Fresh scope — the seeding scope's DbContext still tracks the stale entity.
        await using var readScope = _factory.Services.GetService<IServiceScopeFactory>()!
            .CreateAsyncScope();
        var reader = readScope.ServiceProvider.GetRequiredService<IDelegationService>();
        var updated = await reader.ReadInboxAsync(
            scope, [original.ToAgent], unreadOnly: false, markRead: false);
        updated.Single(m => m.Id == original.Id).ReadAt.ShouldNotBeNull();
    }

    // ---- SPEC-20261007 RF-004: fan-out compare ----

    [Fact]
    public async Task Dado_SemCredenciais_Quando_CompareFanout_Entao_401()
    {
        var response = await _client.GetAsync("/api/local/delegation/fanout/fan-x/compare");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_CompareGrupoInexistente_Entao_404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/local/delegation/fanout/fan-x/compare?scope=itest");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldContain("fanout-group-not-found");
    }

    // ---- SPEC-20261007 RF-003: builtin model probe ----

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetBuiltinModels_Entao_401()
    {
        var response = await _client.GetAsync("/api/agents/builtin/Claude/models");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_CliSemProbe_Quando_GetBuiltinModels_Entao_200ListaVazia()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/agents/builtin/Claude/models");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("models");
    }
}
