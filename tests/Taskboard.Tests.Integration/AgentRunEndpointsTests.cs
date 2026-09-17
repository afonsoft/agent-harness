using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Taskboard.Agents;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260917-agent-eligibility-task-badge: agent runs endpoints + the
/// server-side eligibility guard on POST /api/agents/executions.
/// The factory uses the real home dir, so installed+authenticated CLIs are
/// eligible while OpenHands (no CLI mapping) is structurally ineligible.
/// </summary>
public class AgentRunEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AgentRunEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetRuns_Entao_Retorna401()
    {
        var response = await _client.GetAsync("/api/agents/runs?issueId=x");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetRunsActive_Entao_Retorna401()
    {
        var response = await _client.GetAsync("/api/agents/runs/active");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_AgenteSemCli_Quando_Enqueue_Entao_Retorna422()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var request = CriarRequest("issue-openhands-422", AgentType.OpenHands);

        var response = await client.PostAsJsonAsync("/api/agents/executions", request);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body?["error"]?.GetValue<string>().ShouldBe("agent-not-eligible");
    }

    [Fact]
    public async Task Dado_AgenteElegivel_Quando_Enqueue_Entao_202ERunPersistido()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var issueId = $"issue-run-{Guid.NewGuid():N}";
        var request = CriarRequest(issueId, AgentType.Codex);

        var response = await client.PostAsJsonAsync("/api/agents/executions", request);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var runsResponse = await client.GetAsync($"/api/agents/runs?issueId={issueId}");
        runsResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await runsResponse.Content.ReadFromJsonAsync<JsonObject>();
        var runs = body?["runs"]?.AsArray();
        runs.ShouldNotBeNull();
        runs!.Count.ShouldBeGreaterThanOrEqualTo(1);
        runs[0]?["issueId"]?.GetValue<string>().ShouldBe(issueId);
        runs[0]?["state"]?.GetValue<int>().ShouldBeOneOf(
            (int)AgentRunState.Queued, (int)AgentRunState.Running,
            (int)AgentRunState.Succeeded, (int)AgentRunState.Failed);

        var activeResponse = await client.GetAsync("/api/agents/runs/active");
        activeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var activeBody = await activeResponse.Content.ReadFromJsonAsync<JsonObject>();
        var active = activeBody?["runs"]?.AsArray();
        active.ShouldNotBeNull();
        active!.Any(r => r?["issueId"]?.GetValue<string>() == issueId).ShouldBeTrue();
    }

    private static AgentExecutionRequest CriarRequest(string issueId, AgentType agentType) =>
        new(issueId, 42, "owner/repo", "/tmp", null, null, "instruções", agentType);
}
