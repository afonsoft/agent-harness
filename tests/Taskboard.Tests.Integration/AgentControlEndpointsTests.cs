using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Taskboard.Agents;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260921-board-cockpit-agent-observability RF-004/RF-005: controle
/// unificado (POST /api/agents/control), reply de permissão por escopo e
/// snapshot de estado (GET /api/agents/state).
/// </summary>
public class AgentControlEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AgentControlEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_PostControl_Entao_Retorna401()
    {
        var response = await _client.PostAsJsonAsync("/api/agents/control",
            new { scopeKind = "issue", scopeId = "1", action = "cancel" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_AcaoInvalida_Quando_PostControl_Entao_Retorna400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/agents/control",
            new { scopeKind = "issue", scopeId = "1", action = "explode" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_ThreadSemSessao_Quando_Cancel_Entao_Retorna409()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/agents/control",
            new { scopeKind = "thread", scopeId = "no-such-thread", action = "cancel" });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Dado_SteerSemConteudo_Quando_PostControl_Entao_Retorna400Ou409()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/agents/control",
            new { scopeKind = "run", scopeId = "r1", action = "steer" });

        ((int)response.StatusCode).ShouldBeOneOf(400, 409);
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_ReplyPermission_Entao_Retorna401()
    {
        var response = await _client.PostAsJsonAsync("/api/agents/permissions/reply",
            new { scopeKind = "thread", scopeId = "t1", requestId = "p1", outcome = "allow" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_EscopoIssue_Quando_ReplyPermission_Entao_Retorna409()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/agents/permissions/reply",
            new { scopeKind = "issue", scopeId = "1", requestId = "p1", outcome = "allow" });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Dado_ThreadRequestExpirada_Quando_ReplyPermission_Entao_Retorna410()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/agents/permissions/reply",
            new { scopeKind = "thread", scopeId = "no-such-thread", requestId = "p1", outcome = "allow" });

        response.StatusCode.ShouldBe(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetState_Entao_Retorna401()
    {
        var response = await _client.GetAsync("/api/agents/state?scopeKind=issue&scopeId=1");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_EscopoInvalido_Quando_GetState_Entao_Retorna400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/agents/state?scopeKind=nope&scopeId=1");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_ThreadInexistente_Quando_GetState_Entao_IdleComSequencia()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var state = await client.GetFromJsonAsync<JsonObject>(
            "/api/agents/state?scopeKind=thread&scopeId=no-such-thread");

        state.ShouldNotBeNull();
        state!["state"]!.GetValue<string>().ShouldBe("idle");
        state["lastEventSequence"]!.GetValue<long>().ShouldBeGreaterThanOrEqualTo(0);
    }

    private async Task<string> CreateRunAsync(HttpClient client)
    {
        var created = await client.PostAsJsonAsync("/api/harness/runs",
            new Taskboard.Dtos.RunStartRequest(
                "quick-patch", "afonsoft/agent-harness", "main", null, null, "Implementar JWT"));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<Taskboard.Dtos.PipelineExecutionDto>();
        return dto!.PipelineExecutionId;
    }

    [Fact]
    public async Task Dado_IssueSemExecucao_Quando_Cancel_Entao_Retorna409()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/agents/control",
            new { scopeKind = "issue", scopeId = $"issue-{Guid.NewGuid():N}", action = "cancel" });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Dado_RunInexistente_Quando_CancelOuSteer_Entao_409()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var runId = $"run-{Guid.NewGuid():N}";

        var cancel = await client.PostAsJsonAsync("/api/agents/control",
            new { scopeKind = "run", scopeId = runId, action = "cancel" });
        var steer = await client.PostAsJsonAsync("/api/agents/control",
            new { scopeKind = "run", scopeId = runId, action = "steer", content = "tente de novo" });

        cancel.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        steer.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Dado_RunAtivo_Quando_Steer_Entao_202EEventoAuditado()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var runId = await CreateRunAsync(client);

        var steer = await client.PostAsJsonAsync("/api/agents/control",
            new { scopeKind = "run", scopeId = runId, action = "steer", content = "priorize o refresh token" });

        steer.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var events = await client.GetFromJsonAsync<JsonObject>(
            $"/api/agents/events?scopeKind=run&scopeId={runId}");
        var items = events!["events"]!.AsArray();
        items.ShouldContain(e => e!["kind"]!.GetValue<string>() == "steer"
            && e["title"]!.GetValue<string>() == "Steer queued");
    }

    [Fact]
    public async Task Dado_RunAtivo_Quando_Cancel_Entao_202EEventoAuditado()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var runId = await CreateRunAsync(client);

        var cancel = await client.PostAsJsonAsync("/api/agents/control",
            new { scopeKind = "run", scopeId = runId, action = "cancel" });

        cancel.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var events = await client.GetFromJsonAsync<JsonObject>(
            $"/api/agents/events?scopeKind=run&scopeId={runId}");
        events!["events"]!.AsArray()
            .ShouldContain(e => e!["kind"]!.GetValue<string>() == "lifecycle"
                && e["title"]!.GetValue<string>() == "Cancel requested (run)");
    }

    [Fact]
    public async Task Dado_RunAtivo_Quando_CancelarDuasVezes_Entao_SegundoRetorna409()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var runId = await CreateRunAsync(client);

        await client.PostAsJsonAsync("/api/agents/control",
            new { scopeKind = "run", scopeId = runId, action = "cancel" });
        var again = await client.PostAsJsonAsync("/api/agents/control",
            new { scopeKind = "run", scopeId = runId, action = "cancel" });

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Dado_RunPausado_Quando_GetState_Entao_EstadoPaused()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var runId = await CreateRunAsync(client);
        await client.PostAsJsonAsync($"/api/harness/runs/{runId}/pause", (object?)null);

        var state = await client.GetFromJsonAsync<JsonObject>(
            $"/api/agents/state?scopeKind=run&scopeId={runId}");

        state!["state"]!.GetValue<string>().ShouldBe("paused");
    }

    [Fact]
    public async Task Dado_GateStage_Quando_ReplyDeny_Entao_200EStageFailed()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var runId = await CreateRunAsync(client);

        var reply = await client.PostAsJsonAsync("/api/agents/permissions/reply",
            new { scopeKind = "run", scopeId = runId, requestId = "stage:builder", outcome = "deny", comment = "plano incompleto" });

        reply.StatusCode.ShouldBe(HttpStatusCode.OK);
        var exec = await reply.Content.ReadFromJsonAsync<JsonObject>();
        exec!["stages"]!.AsArray()
            .Single(s => s!["stageKey"]!.GetValue<string>() == "builder")!
            ["status"]!.GetValue<string>().ShouldBe("Failed");
    }

    [Fact]
    public async Task Dado_RunInexistente_Quando_ReplyPermission_Entao_409()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var reply = await client.PostAsJsonAsync("/api/agents/permissions/reply",
            new { scopeKind = "run", scopeId = $"run-{Guid.NewGuid():N}", requestId = "stage:builder", outcome = "allow" });

        reply.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Dado_EventosEmitidos_Quando_GetState_Entao_MaxSequenciaRefletida()
    {
        var scopeId = $"run-{Guid.NewGuid():N}";
        var sink = _factory.Services.GetRequiredService<IAgentExecutionEventSink>();
        await sink.EmitAsync(new AgentExecutionEvent(
            string.Empty, "run", scopeId, 0, DateTimeOffset.UtcNow, "lifecycle"));

        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"/api/agents/state?scopeKind=run&scopeId={scopeId}");

        // run inexistente → 404, mas a sequência deve estar persistida para replay
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var events = await client.GetFromJsonAsync<JsonObject>(
                $"/api/agents/events?scopeKind=run&scopeId={scopeId}");
            events!["events"]!.AsArray().Count.ShouldBe(1);
            return;
        }

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var state = await response.Content.ReadFromJsonAsync<JsonObject>();
        state!["lastEventSequence"]!.GetValue<long>().ShouldBeGreaterThan(0);
    }
}
