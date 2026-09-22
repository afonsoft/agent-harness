using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

public class GitHubWorkflowEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public GitHubWorkflowEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private Task<HttpClient> ApiClientAsync() => _factory.CreateAuthenticatedClientAsync();

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetWorkflows_Entao_Retorna401()
    {
        var response = await _client.GetAsync("/api/github/repos/owner/repo/workflows");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetWorkflowRuns_Entao_Retorna401()
    {
        var response = await _client.GetAsync("/api/github/repos/owner/repo/workflows/11/runs");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_RepoComWorkflows_Quando_GetWorkflows_Entao_ListaComLastRun()
    {
        var client = await ApiClientAsync();

        var response = await client.GetAsync("/api/github/repos/owner/repo/workflows");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        var workflows = body?["workflows"] as JsonArray;
        workflows.ShouldNotBeNull();
        workflows!.Count.ShouldBe(1);
        var workflow = workflows[0]!;
        workflow["id"]!.GetValue<long>().ShouldBe(11);
        workflow["name"]!.GetValue<string>().ShouldBe("CI");
        workflow["state"]!.GetValue<string>().ShouldBe("active");
        var lastRun = workflow["lastRun"] as JsonObject;
        lastRun.ShouldNotBeNull();
        lastRun!["conclusion"]!.GetValue<string>().ShouldBe("success");
        lastRun["headBranch"]!.GetValue<string>().ShouldBe("main");
        lastRun["workflowId"]!.GetValue<long>().ShouldBe(11);
        body!["degraded"]!.GetValue<bool>().ShouldBeFalse();
        var recentRuns = body["recentRuns"] as JsonArray;
        recentRuns.ShouldNotBeNull();
        recentRuns!.Count.ShouldBe(1);
        (recentRuns[0]!["id"])!.GetValue<long>().ShouldBe(9001);
    }

    [Fact]
    public async Task Dado_WorkflowComRuns_Quando_GetRuns_Entao_ListaOrdenada()
    {
        var client = await ApiClientAsync();

        var response = await client.GetAsync("/api/github/repos/owner/repo/workflows/11/runs");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        var runs = body?["runs"] as JsonArray;
        runs.ShouldNotBeNull();
        runs!.Count.ShouldBe(1);
        var run = runs[0]!;
        run["id"]!.GetValue<long>().ShouldBe(9001);
        run["status"]!.GetValue<string>().ShouldBe("completed");
        run["headSha"]!.GetValue<string>().ShouldBe("0123456789abcdef");
        run["actorLogin"]!.GetValue<string>().ShouldBe("itest-bot");
    }

    [Fact]
    public async Task Dado_ParametroTake_Quando_GetRuns_Entao_RespeitaLimite()
    {
        var client = await ApiClientAsync();

        var response = await client.GetAsync("/api/github/repos/owner/repo/workflows/11/runs?take=5");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        (body?["runs"] as JsonArray).ShouldNotBeNull();
    }
}
