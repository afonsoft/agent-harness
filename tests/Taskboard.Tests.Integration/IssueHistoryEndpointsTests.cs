using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260918-issue-history: board mutations on a GitHub issue (column move,
/// edit, close) are persisted as history events and surfaced, merged with agent
/// runs, through <c>GET /api/github/issues/{issueId}/history</c>. The fake
/// <see cref="Taskboard.GitHub.IGitHubService"/> always returns issue Id = 1, so
/// all recorded events land under issueId "1".
/// </summary>
public class IssueHistoryEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private const string IssueBase = "/api/github/repos/owner/repo/issues/42";
    private const string HistoryUrl = "/api/github/issues/1/history";

    private readonly TaskboardWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public IssueHistoryEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetHistory_Entao_Retorna401()
    {
        var response = await _client.GetAsync(HistoryUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_GetHistory_Entao_RetornaLista()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(HistoryUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body.ShouldNotBeNull();
        body["items"].ShouldNotBeNull();
    }

    [Fact]
    public async Task Dado_MoveDeColuna_Quando_GetHistory_Entao_ContemColumnMoved()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var move = await client.PutAsJsonAsync($"{IssueBase}/column", new { oldColumn = 1, newColumn = 2 });
        move.StatusCode.ShouldBe(HttpStatusCode.OK);

        var items = await GetItemsAsync(client);
        var moved = items.FirstOrDefault(i => i["kind"]?.GetValue<string>() == "column-moved");
        moved.ShouldNotBeNull();
        moved["from"]?.GetValue<string>().ShouldBe("todo");
        moved["to"]?.GetValue<string>().ShouldBe("in-progress");
    }

    [Fact]
    public async Task Dado_EdicaoDeIssue_Quando_GetHistory_Entao_ContemEdited()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var patch = new HttpRequestMessage(HttpMethod.Patch, IssueBase)
        {
            Content = JsonContent.Create(new { title = "novo título", body = "novo corpo" }),
        };
        (await client.SendAsync(patch)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var items = await GetItemsAsync(client);
        var edited = items.FirstOrDefault(i => i["kind"]?.GetValue<string>() == "edited");
        edited.ShouldNotBeNull();
        edited["detail"]?.GetValue<string>().ShouldContain("title");
        edited["detail"]?.GetValue<string>().ShouldContain("body");
    }

    [Fact]
    public async Task Dado_FechamentoDeIssue_Quando_GetHistory_Entao_ContemClosed()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var close = await client.PostAsJsonAsync($"{IssueBase}/close", new { resolution = "canceled" });
        close.StatusCode.ShouldBe(HttpStatusCode.OK);

        var items = await GetItemsAsync(client);
        var closed = items.FirstOrDefault(i => i["kind"]?.GetValue<string>() == "closed");
        closed.ShouldNotBeNull();
        closed["detail"]?.GetValue<string>().ShouldBe("canceled");
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_VscodeOpen_Entao_Retorna401()
    {
        var response = await _client.GetAsync("/api/vscode/open?repo=owner/repo");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_RepoValido_Quando_VscodeOpen_Entao_RedirectParaEditor()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/vscode/open?repo=afonsoft/taskboard-ai");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location?.ToString();
        location.ShouldNotBeNull();
        location.ShouldStartWith("/vscode/?folder=");
    }

    [Fact]
    public async Task Dado_RepoInvalido_Quando_VscodeOpen_Entao_Retorna404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/vscode/open?repo=no-owner-segment");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<List<JsonNode>> GetItemsAsync(HttpClient client)
    {
        var response = await client.GetAsync(HistoryUrl);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        return body!["items"]!.AsArray()
            .Where(i => i is not null)
            .Select(i => i!)
            .ToList();
    }
}
