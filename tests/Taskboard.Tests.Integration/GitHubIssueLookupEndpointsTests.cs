using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// GET /api/github/repos/{owner}/{repo}/issues/{number} — direct issue lookup
/// for prompt context (closed/old issues that GetIssuesAsync's visibility
/// window would miss).
/// </summary>
public class GitHubIssueLookupEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public GitHubIssueLookupEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetIssue_Entao_Retorna401()
    {
        var response = await _client.GetAsync("/api/github/repos/owner/repo/issues/42");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_IssueExistente_Quando_GetIssue_Entao_RetornaIssue()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/github/repos/owner/repo/issues/42");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<IssuePayload>();
        payload.ShouldNotBeNull();
        payload.Issue.Number.ShouldBe(42);
        payload.Issue.Title.ShouldBe("itest issue");
    }

    [Fact]
    public async Task Dado_IssueInexistente_Quando_GetIssue_Entao_Retorna404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/github/repos/owner/repo/issues/9999");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private sealed record IssuePayload(IssueItem Issue);
    private sealed record IssueItem(int Number, string Title);
}
