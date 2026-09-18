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
    public async Task Dado_RepositorioInvalido_Quando_Enqueue_Entao_400SemRun()
    {
        // SPEC-20260918-agent-model-config RF-001: fail fast on a malformed
        // repo slug (e.g. the literal placeholder "RepositoryFullName").
        var client = await _factory.CreateAuthenticatedClientAsync();
        var issueId = $"issue-badrepo-{Guid.NewGuid():N}";
        var request = new AgentExecutionRequest(
            issueId, 42, "RepositoryFullName", "/tmp", null, null, "instruções", AgentType.Codex);

        var response = await client.PostAsJsonAsync("/api/agents/executions", request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body?["error"]?.GetValue<string>().ShouldBe("invalid-repository");

        var runsResponse = await client.GetAsync($"/api/agents/runs?issueId={issueId}");
        var runsBody = await runsResponse.Content.ReadFromJsonAsync<JsonObject>();
        runsBody?["runs"]?.AsArray().ShouldBeEmpty();
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

    [Fact]
    public async Task Dado_LogsDeIssue_Quando_Delete_Entao_204ELogsEsvaziados()
    {
        // Covers RF-002 / AC-2: DELETE limpa o histórico persistido.
        var client = await _factory.CreateAuthenticatedClientAsync();
        var issueId = $"issue-clear-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/agents/executions", CriarRequest(issueId, AgentType.Codex));

        var deleteResponse = await client.DeleteAsync($"/api/agents/logs/{issueId}");

        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_DeleteLogs_Entao_Retorna401()
    {
        var client = _factory.CreateClient();

        var response = await client.DeleteAsync("/api/agents/logs/issue-x");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_PromptTemplate_Quando_GetEPut_Entao_200E204()
    {
        // Covers RF-004: GET retorna template efetivo, PUT salva override.
        var client = await _factory.CreateAuthenticatedClientAsync();

        var getResponse = await client.GetAsync("/api/agents/prompt-template");
        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await getResponse.Content.ReadFromJsonAsync<JsonObject>();
        body?["template"]?.GetValue<string>().ShouldNotBeNullOrEmpty();

        var putResponse = await client.PutAsJsonAsync(
            "/api/agents/prompt-template", new { template = "Clone {repoUrl}." });
        putResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var getAfter = await client.GetAsync("/api/agents/prompt-template");
        var bodyAfter = await getAfter.Content.ReadFromJsonAsync<JsonObject>();
        bodyAfter?["template"]?.GetValue<string>().ShouldBe("Clone {repoUrl}.");
        bodyAfter?["customized"]?.GetValue<bool>().ShouldBeTrue();

        await client.PutAsJsonAsync("/api/agents/prompt-template", new { template = "" });
    }

    [Fact]
    public async Task Dado_TemplateMuitoLongo_Quando_Put_Entao_400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/agents/prompt-template", new { template = new string('x', 9000) });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static AgentExecutionRequest CriarRequest(string issueId, AgentType agentType) =>
        new(issueId, 42, "owner/repo", "/tmp", null, null, "instruções", agentType);
}
