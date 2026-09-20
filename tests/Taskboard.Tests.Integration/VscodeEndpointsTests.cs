using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260917-vscode-web-workspace: the code-server status/install/workdir
/// endpoints are authenticated surfaces and workdir resolution is confined to
/// the configured workspace root.
/// </summary>
public class VscodeEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public VscodeEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetVscodeStatus_Entao_Retorna401()
    {
        var response = await _client.GetAsync("/api/vscode/status");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_PostVscodeInstall_Entao_Retorna401()
    {
        var response = await _client.PostAsync("/api/vscode/install", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetVscodeWorkdir_Entao_Retorna401()
    {
        var response = await _client.GetAsync("/api/vscode/workdir?repo=a/b");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_GetVscodeStatus_Entao_RetornaSnapshot()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/vscode/status");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body.ShouldNotBeNull();
        body["installed"]!.GetValue<bool>().ShouldBeFalse();
        body["running"]!.GetValue<bool>().ShouldBeFalse();
        body["port"]!.GetValue<int>().ShouldBeGreaterThan(0);
        body["workspaceRoot"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_PostInstall_Entao_RetornaStatusComLinhas()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync("/api/vscode/install", null);

        ((int)response.StatusCode).ShouldBeOneOf(200, 202);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body.ShouldNotBeNull();
        body["state"].ShouldNotBeNull();
        body["lines"].ShouldNotBeNull();
    }

    [Fact]
    public async Task Dado_RepoValido_Quando_GetWorkdir_Entao_RetornaCaminhoDentroDoRoot()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/vscode/workdir?repo=afonsoft/taskboard-ai");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        var path = body!["path"]!.GetValue<string>();
        var exists = body["exists"]!.GetValue<bool>();
        // Repo dir doesn't exist in the test root → falls back to the root itself.
        exists.ShouldBeFalse();
        path.ShouldContain("repos");
        Path.IsPathRooted(path).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_RepoInvalido_Quando_GetWorkdir_Entao_Retorna404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/vscode/workdir?repo=no-owner-segment");
        var traversal = await client.GetAsync("/api/vscode/workdir?repo=" + Uri.EscapeDataString("../../etc"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        traversal.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dado_InstallStatus_Quando_GetStatus_Entao_RetornaSnapshot()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/vscode/install/status");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body.ShouldNotBeNull();
        body["state"].ShouldNotBeNull();
    }

    // SPEC-20260920-global-repo-selector RF-008 — POST /api/vscode/restart.

    [Fact]
    public async Task Dado_SemCredenciais_Quando_PostVscodeRestart_Entao_Retorna401()
    {
        var response = await _client.PostAsync("/api/vscode/restart", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_PostVscodeRestart_Entao_200ComStatus()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync("/api/vscode/restart", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body.ShouldNotBeNull();
        body["running"]!.GetValue<bool>().ShouldBeFalse();
    }
}
