using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260918-gantt-github-timeline RF-005: GET timeline/metrics are
/// admin-only and return the GitHub-backed timeline (issues, transitions,
/// milestones) plus the kanban flow metrics.
/// </summary>
public class GitHubTimelineEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private const string TimelineUrl = "/api/github/repos/owner/repo/timeline";
    private const string MetricsUrl = "/api/github/repos/owner/repo/metrics";

    private readonly TaskboardWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public GitHubTimelineEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetTimeline_Entao_Retorna401()
    {
        var response = await _client.GetAsync(TimelineUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetMetrics_Entao_Retorna401()
    {
        var response = await _client.GetAsync(MetricsUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_GetTimeline_Entao_RetornaIssuesTransicoesEMilestones()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(TimelineUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonObject>();
        payload.ShouldNotBeNull();

        var issues = payload["issues"]!.AsArray();
        issues.Count.ShouldBe(1);
        issues[0]!["number"]!.GetValue<int>().ShouldBe(42);
        issues[0]!["column"]!.GetValue<string>().ShouldBe("todo");

        // fake service returns todo@-10d + in-progress@-5d label events
        var transitions = issues[0]!["transitions"]!.AsArray();
        transitions.Count.ShouldBe(2);
        transitions[0]!["to"]!.GetValue<string>().ShouldBe("todo");
        transitions[1]!["to"]!.GetValue<string>().ShouldBe("in-progress");

        var milestones = payload["milestones"]!.AsArray();
        milestones.Count.ShouldBe(1);
        milestones[0]!["title"]!.GetValue<string>().ShouldBe("v1.0");
        milestones[0]!["dueOn"].ShouldNotBeNull();
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_GetMetrics_Entao_RetornaAgregados()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(MetricsUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonObject>();
        payload.ShouldNotBeNull();

        // single open issue in the "todo" column → WIP 1
        payload["wip"]!.GetValue<int>().ShouldBe(1);
        payload["openMedianAgeDays"].ShouldNotBeNull();

        var throughput = payload["throughputPerWeek"]!.AsArray();
        throughput.Count.ShouldBe(8);
        throughput.ShouldAllBe(p => p!["closed"]!.GetValue<int>() == 0);
    }

    [Fact]
    public async Task Dado_JanelaCustomizada_Quando_GetTimeline_Entao_AceitaParametroDays()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync($"{TimelineUrl}?days=30");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
