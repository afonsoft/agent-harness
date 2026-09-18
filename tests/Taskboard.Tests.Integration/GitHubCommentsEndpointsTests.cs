using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260918-github-comments-history RF-001: GET/POST issue comments are
/// admin-only, validate input, and proxy the stubbed
/// <see cref="Taskboard.GitHub.IGitHubService"/>.
/// </summary>
public class GitHubCommentsEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private const string CommentsUrl = "/api/github/repos/owner/repo/issues/42/comments";

    private readonly TaskboardWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public GitHubCommentsEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetComments_Entao_Retorna401()
    {
        var response = await _client.GetAsync(CommentsUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_PostComment_Entao_Retorna401()
    {
        var response = await _client.PostAsJsonAsync(CommentsUrl, new { body = "x" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_BodyVazio_Quando_PostComment_Entao_Retorna400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var empty = await client.PostAsJsonAsync(CommentsUrl, new { body = "" });
        var whitespace = await client.PostAsJsonAsync(CommentsUrl, new { body = "   " });

        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        whitespace.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_BodyValido_Quando_PostComment_Entao_CriaComentario()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(CommentsUrl, new { body = "handoff: etapa 1 concluída" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        var comment = body?["comment"];
        comment.ShouldNotBeNull();
        comment["body"]!.GetValue<string>().ShouldBe("handoff: etapa 1 concluída");
        comment["authorLogin"]!.GetValue<string>().ShouldBe("itest-bot");
    }

    [Fact]
    public async Task Dado_ComentarioPostado_Quando_GetComments_Entao_ApareceNaLista()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        await client.PostAsJsonAsync(CommentsUrl, new { body = "comentário para o próximo agente" });
        var response = await client.GetAsync(CommentsUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        var comments = body!["comments"]!.AsArray();
        comments.ShouldNotBeEmpty();
        comments.Any(c => c!["body"]!.GetValue<string>() == "comentário para o próximo agente")
            .ShouldBeTrue();
    }
}
