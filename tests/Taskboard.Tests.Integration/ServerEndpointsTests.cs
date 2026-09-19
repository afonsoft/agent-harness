using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;
using Taskboard.Server;

namespace Taskboard.Tests.Integration;

public class ServerEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TaskboardWebApplicationFactory _factory;

    public ServerEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // /api requires a session since SPEC-20260915-api-authorization-hardening
    private Task<HttpClient> ApiClientAsync() => _factory.CreateAuthenticatedClientAsync();

    [Fact]
    public async Task Given_NoAuth_When_GetHealth_Then_Returns200()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Given_NoAuth_When_GetSwaggerJson_Then_ReturnsOpenApi()
    {
        // Covers FR-001: Swagger JSON is reachable without authentication
        var response = await _client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonObject>();
        result.ShouldNotBeNull();
        result["openapi"].ShouldNotBeNull();
    }

    [Fact]
    public async Task Given_NoAuth_When_GetRoot_Then_Returns200OrRedirect()
    {
        var response = await _client.GetAsync("/");

        // Root may serve Blazor app or redirect to login
        response.StatusCode.ShouldBeOneOf(System.Net.HttpStatusCode.OK, System.Net.HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Given_ExistingSkill_When_GetSkillDetail_Then_ReturnsSkillContent()
    {
        // Covers FR-003: skill detail API
        var client = await ApiClientAsync();
        var response = await client.GetAsync("/api/skills/taskboard/manage-taskboard");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonObject>();
        result.ShouldNotBeNull();
        result["skill"].ShouldNotBeNull();
        var content = result["skill"]?["content"]?.GetValue<string>();
        content.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Given_ExistingSkill_When_GetSkillDetail_Then_ReturnsFileTree()
    {
        // Covers RF-006: skill detail includes the file list
        var client = await ApiClientAsync();
        var response = await client.GetAsync("/api/skills/taskboard/manage-taskboard");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonObject>();
        var files = result?["skill"]?["files"] as JsonArray;
        files.ShouldNotBeNull();
        var paths = files!.Select(f => f!["relativePath"]!.GetValue<string>()).ToList();
        paths.ShouldContain("SKILL.md");
        paths.ShouldContain("references/cli.md");
    }

    [Fact]
    public async Task Given_ExistingSkillFile_When_GetSkillFile_Then_ReturnsContent()
    {
        // Covers RF-007: file content endpoint returns text content
        var client = await ApiClientAsync();
        var response = await client.GetAsync("/api/skills/taskboard/manage-taskboard/files/references/cli.md");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonObject>();
        result.ShouldNotBeNull();
        result["path"]?.GetValue<string>().ShouldBe("references/cli.md");
        result["content"]?.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("/api/skills/taskboard/manage-taskboard/files/..%2Fadmin.json")]
    [InlineData("/api/skills/taskboard/manage-taskboard/files/references%2F..%2F..%2Fetc%2Fpasswd")]
    public async Task Given_TraversalPath_When_GetSkillFile_Then_Returns400(string url)
    {
        // Covers RF-007: path traversal is rejected
        var client = await ApiClientAsync();
        var response = await client.GetAsync(url);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Given_MissingSkillFile_When_GetSkillFile_Then_Returns404()
    {
        // Covers RF-007: missing file returns 404
        var client = await ApiClientAsync();
        var response = await client.GetAsync("/api/skills/taskboard/manage-taskboard/files/does-not-exist.md");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dado_EndpointRemovido_Quando_GetApiProjects_Entao_Retorna404NaoHtml()
    {
        var client = await ApiClientAsync();

        var response = await client.GetAsync("/api/projects");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldNotBe("text/html");
    }

    [Fact]
    public async Task Given_ServerRunning_When_GetBlazorWebJs_Then_Returns200()
    {
        // Regression: _framework/blazor.web.js must be served as a static web asset
        // (RequiresAspNetWebAssets in Taskboard.Server.csproj). Without it the
        // sidebar NavLinks are dead — see SPEC-20260914-blazor-web-assets.
        var response = await _client.GetAsync("/_framework/blazor.web.js");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Dado_RequisicaoComAcceptEncodingGzip_Quando_GetRepositories_Entao_RetornaConteudoComprimido()
    {
        // Covers FR-006: response compression for dynamic responses
        var client = await ApiClientAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/github/repositories");
        request.Headers.AcceptEncoding.Add(new System.Net.Http.Headers.StringWithQualityHeaderValue("gzip"));

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.ShouldContain("gzip");
    }
}
