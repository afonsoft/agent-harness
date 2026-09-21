using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Taskboard.Agents;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260921-agent-execution-event-pipeline RF-003: replay paginado de
/// eventos normalizados via GET /api/agents/events.
/// </summary>
public class AgentEventsEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AgentEventsEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetEvents_Entao_Retorna401()
    {
        var response = await _client.GetAsync("/api/agents/events?scopeKind=run&scopeId=r1");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_ScopeInvalido_Quando_GetEvents_Entao_Retorna400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/agents/events?scopeKind=nope&scopeId=r1");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_EventosPersistidos_Quando_GetEvents_Entao_ReplayOrdenadoPaginado()
    {
        var scopeId = $"run-{Guid.NewGuid():N}";
        var sink = _factory.Services.GetRequiredService<IAgentExecutionEventSink>();
        for (var i = 0; i < 3; i++)
        {
            await sink.EmitAsync(new AgentExecutionEvent(
                string.Empty, "run", scopeId, 0, DateTimeOffset.UtcNow,
                "output", PayloadJson: $"{{\"line\":{i}}}"));
        }

        var client = await _factory.CreateAuthenticatedClientAsync();

        var firstPage = await client.GetFromJsonAsync<JsonObject>(
            $"/api/agents/events?scopeKind=run&scopeId={scopeId}&take=2");
        firstPage.ShouldNotBeNull();
        firstPage["events"]!.AsArray().Count.ShouldBe(2);
        firstPage["hasMore"]!.GetValue<bool>().ShouldBeTrue();

        var nextAfter = firstPage["nextAfter"]!.GetValue<long>();
        var secondPage = await client.GetFromJsonAsync<JsonObject>(
            $"/api/agents/events?scopeKind=run&scopeId={scopeId}&after={nextAfter}");
        var second = secondPage!["events"]!.AsArray();
        second.Count.ShouldBe(1);
        second[0]!["sequence"]!.GetValue<long>().ShouldBeGreaterThan(nextAfter);
    }
}
