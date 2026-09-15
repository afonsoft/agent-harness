using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260915-blazor-wasm-migration: SPA shell, framework-assets proxy and
/// API authorization for the Blazor WebAssembly host.
/// </summary>
public class WasmHostingTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly HttpClient _client;

    public WasmHostingTests(TaskboardWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Given_NoAuth_When_GetRoot_Then_ReturnsSpaShell()
    {
        var response = await _client.GetAsync("/");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("id=\"app\"");
        body.ShouldContain("blazor.webassembly.js");
    }

    [Fact]
    public async Task Given_NoAuth_When_GetLogin_Then_ReturnsSpaShell()
    {
        // The SPA owns /login client-side; the server must not HTML-redirect it.
        var response = await _client.GetAsync("/login");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("id=\"app\"");
    }

    [Fact]
    public async Task Given_NoAuth_When_GetAuthMe_Then_Returns401()
    {
        var response = await _client.GetAsync("/api/auth/me");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Given_NoAuth_When_GetGitHubRepositories_Then_Returns401()
    {
        var response = await _client.GetAsync("/api/github/repositories");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Given_NoAuth_When_GetAgents_Then_Returns401()
    {
        var response = await _client.GetAsync("/api/agents");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Given_NoAuth_When_PostAgentLogHubNegotiate_Then_Returns401NotHtmlRedirect()
    {
        // Regression: the Blazor Server circuit died because negotiate received
        // the HTML login page. The hub must answer 401, never a redirect.
        var response = await _client.PostAsync("/agent-log-hub/negotiate?negotiateVersion=1", null);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Given_FrameworkAsset_When_GetViaProxy_Then_ReturnsAsset()
    {
        var response = await _client.GetAsync("/framework-assets/blazor.web/js");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/javascript");
    }

    [Fact]
    public async Task Given_FrameworkAsset_When_GetBase64_Then_ReturnsTextPayload()
    {
        var response = await _client.GetAsync("/framework-assets/blazor.web/js?enc=b64");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/plain");
        var body = await response.Content.ReadAsStringAsync();
        Convert.FromBase64String(body.Trim()).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("/framework-assets/..%2Fsecret/json")]
    [InlineData("/framework-assets/evil$stem/json")]
    [InlineData("/framework-assets/blazor.boot/EXE")]
    [InlineData("/framework-assets/does-not-exist/json")]
    public async Task Given_InvalidFrameworkAssetPath_When_Get_Then_Returns404(string url)
    {
        var response = await _client.GetAsync(url);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
    }
}
