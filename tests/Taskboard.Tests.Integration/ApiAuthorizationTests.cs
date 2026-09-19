using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260915-api-authorization-hardening: regression suite for the
/// anonymous /api surface left by the WASM migration. Every non-allowlisted
/// endpoint must answer 401 without credentials, and accept either the
/// cookie session or X-Api-Key.
/// </summary>
public class ApiAuthorizationTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ApiAuthorizationTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/api/settings")]
    [InlineData("/api/configuration")]
    [InlineData("/api/skills")]
    [InlineData("/api/local/jira-connection")]
    [InlineData("/api/local/ai/threads")]
    [InlineData("/api/github/repositories")]
    [InlineData("/api/projects")]
    [InlineData("/api/agents")]
    [InlineData("/api/skills/sync/status")]
    [InlineData("/api/events")]
    public async Task Dado_SemCredenciais_Quando_GetApiProtegida_Entao_Retorna401(string url)
    {
        var response = await _client.GetAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/skills/sync")]
    [InlineData("/api/local/ai/threads")]
    public async Task Dado_SemCredenciais_Quando_PostMutacao_Entao_Retorna401(string url)
    {
        var response = await _client.PostAsJsonAsync(url, new { name = "anon", title = "anon" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/settings")]
    [InlineData("/api/configuration/Taskboard:Port")]
    public async Task Dado_SemCredenciais_Quando_PutMutacao_Entao_Retorna401(string url)
    {
        var response = await _client.PutAsJsonAsync(url, new { value = "x" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetMeta_Entao_Retorna200()
    {
        // Allowlist: meta is the pre-auth version/transport probe
        var response = await _client.GetAsync("/api/meta");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetAuthMe_Entao_Retorna401SemBloqueio()
    {
        // Allowlist: auth/me is reachable anonymously and reports 401 itself
        var response = await _client.GetAsync("/api/auth/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_PostLoginInvalido_Entao_Retorna401NaoBloqueado()
    {
        // Allowlist: login must be reachable anonymously (bad creds → 401 body, not an auth wall)
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.PostAsync(
            "/api/login",
            new FormUrlEncodedContent([
                new KeyValuePair<string, string>("Username", "admin"),
                new KeyValuePair<string, string>("Password", "wrong"),
            ]));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_ApiKeyValida_Quando_GetRepositories_Entao_Retorna200()
    {
        var client = _factory.CreateApiKeyClient();

        var response = await client.GetAsync("/api/github/repositories");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Dado_ApiKeyValida_Quando_GetSettings_Entao_Retorna200()
    {
        var client = _factory.CreateApiKeyClient();

        var response = await client.GetAsync("/api/settings");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Dado_ApiKeyInvalida_Quando_GetRepositories_Entao_Retorna401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key-0000000000");

        var response = await client.GetAsync("/api/github/repositories");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_CookieLogin_Quando_GetRepositories_Entao_Retorna200()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/github/repositories");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Dado_CookieLogin_Quando_GetAuthMe_Entao_RetornaUsuario()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body?["authenticated"]?.GetValue<bool>().ShouldBeTrue();
        body?["username"]?.GetValue<string>().ShouldBe("admin");
    }

    [Fact]
    public async Task Dado_ConfiguracaoComApiKey_Quando_GetConfiguration_Entao_ValorMascarado()
    {
        // RF-006: the ApiKey entry must never serialize the plaintext key
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/configuration");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        var entries = body?["entries"] as JsonArray;
        entries.ShouldNotBeNull();
        var apiKey = entries!.FirstOrDefault(e => e?["key"]?.GetValue<string>() == "Taskboard:ApiKey");
        apiKey.ShouldNotBeNull();
        apiKey!["masked"]?.GetValue<bool>().ShouldBeTrue();
        apiKey["value"]?.GetValue<string>().ShouldNotContain(TaskboardWebApplicationFactory.TestApiKey);
    }
}
