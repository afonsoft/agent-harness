using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260918-kanban-card-ux: PATCH issue, PUT priority and POST close are
/// admin-only, validate enum inputs and proxy a stubbed <see cref="Taskboard.GitHub.IGitHubService"/>.
/// </summary>
public class GitHubIssueMutationEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private const string IssueBase = "/api/github/repos/owner/repo/issues/42";

    private readonly TaskboardWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public GitHubIssueMutationEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_MutacoesDeIssue_Entao_Retorna401()
    {
        var patch = new HttpRequestMessage(HttpMethod.Patch, IssueBase)
        {
            Content = JsonContent.Create(new { title = "t", body = "b" }),
        };
        (await _client.SendAsync(patch)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _client.PutAsJsonAsync($"{IssueBase}/priority", new { priority = "high" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _client.PostAsJsonAsync($"{IssueBase}/close", new { resolution = "canceled" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_PrioridadeInvalida_Quando_SetPriority_Entao_Retorna400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync($"{IssueBase}/priority", new { priority = "critical" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body?["error"]?.GetValue<string>().ShouldBe("invalid-priority");
    }

    [Fact]
    public async Task Dado_PrioridadeValida_Quando_SetPriority_Entao_Retorna200ComIssue()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync($"{IssueBase}/priority", new { priority = "high" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body?["issue"]?["number"]?.GetValue<int>().ShouldBe(42);
    }

    [Fact]
    public async Task Dado_ResolucaoInvalida_Quando_Close_Entao_Retorna400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync($"{IssueBase}/close", new { resolution = "duplicate" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body?["error"]?.GetValue<string>().ShouldBe("invalid-resolution");
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("archived")]
    public async Task Dado_ResolucaoValida_Quando_Close_Entao_Retorna200ComIssue(string resolution)
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync($"{IssueBase}/close", new { resolution });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body?["issue"]?["number"]?.GetValue<int>().ShouldBe(42);
    }

    [Fact]
    public async Task Dado_BodyMarkdown_Quando_PatchIssue_Entao_Retorna200ComIssue()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var request = new HttpRequestMessage(HttpMethod.Patch, IssueBase)
        {
            Content = JsonContent.Create(new { title = "novo título", body = "## corpo" }),
        };

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body?["issue"]?["number"]?.GetValue<int>().ShouldBe(42);
    }
}
