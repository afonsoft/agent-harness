using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Taskboard.Server;
using Xunit;

namespace Taskboard.Tests.Integration;

public class McpEndpointsTests : IClassFixture<McpEndpointsTests.AuthenticatedFactory>
{
    private const string AdminPassword = "itest-admin-pass";

    private readonly AuthenticatedFactory _factory;
    private readonly HttpClient _client;

    public McpEndpointsTests(AuthenticatedFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public sealed class AuthenticatedFactory : WebApplicationFactory<Program>
    {
        private HttpClient? _authedClient;

        public string DataDir { get; } = Path.Combine(Path.GetTempPath(), $"tb-itest-{Guid.NewGuid()}");
        public string HomeDir { get; } = Path.Combine(Path.GetTempPath(), $"tb-itest-home-{Guid.NewGuid()}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Taskboard:DataDir", DataDir);
            builder.UseSetting("Taskboard:HomeDir", HomeDir);
            builder.UseSetting("Admin:Password", AdminPassword);
            builder.UseSetting("Taskboard:Skills:SyncOnStartup", "false");
        }

        public async Task<HttpClient> CreateAuthenticatedClientAsync()
        {
            if (_authedClient is not null)
            {
                return _authedClient;
            }

            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true,
            });
            var login = await client.PostAsync(
                "/api/login",
                new FormUrlEncodedContent([
                    new KeyValuePair<string, string>("Username", "admin"),
                    new KeyValuePair<string, string>("Password", AdminPassword),
                ]));
            if (login.StatusCode != HttpStatusCode.Redirect)
            {
                throw new InvalidOperationException(
                    $"Test login failed with {(int)login.StatusCode}: {await login.Content.ReadAsStringAsync()}");
            }

            _authedClient = client;
            return client;
        }
    }

    private Task<HttpClient> CreateAuthenticatedClientAsync() => _factory.CreateAuthenticatedClientAsync();

    [Fact]
    public async Task Dado_SemAutenticacao_Quando_GetMcpStatus_Entao_401()
    {
        var response = await _client.GetAsync("/api/mcp/status");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_SemAutenticacao_Quando_PostMcpSync_Entao_401()
    {
        var response = await _client.PostAsync("/api/mcp/sync", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_SemAutenticacao_Quando_PutMcpRag_Entao_401()
    {
        var response = await _client.PutAsJsonAsync(
            "/api/mcp/rag", new { name = "knowledge", url = "https://rag.example.com/mcp" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_UrlInvalida_Quando_PutMcpRag_Entao_400Validation()
    {
        // Covers RF-007/edge: bad URL rejected, nothing persisted
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/mcp/rag", new { name = "knowledge", url = "notaurl", apiKey = (string?)null });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var result = await response.Content.ReadFromJsonAsync<JsonObject>();
        result!["error"]!["code"]!.GetValue<string>().ShouldBe("VALIDATION");
    }

    [Fact]
    public async Task Dado_ConfigValida_Quando_PutMcpRag_Entao_204EProvisionaArquivos()
    {
        // Covers RF-004/AC: save persists + background provision writes agent files
        var client = await CreateAuthenticatedClientAsync();

        var put = await client.PutAsJsonAsync("/api/mcp/rag", new
        {
            name = "knowledge",
            url = "https://rag.afonsoft.dev/mcp",
            apiKey = "aft_integration_test_key"
        });
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Provision runs in background — poll the status endpoint.
        var claudeConfig = Path.Combine(_factory.HomeDir, ".claude.json");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!File.Exists(claudeConfig) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
        }

        File.Exists(claudeConfig).ShouldBeTrue();
        var json = JsonNode.Parse(File.ReadAllText(claudeConfig))!.AsObject();
        var entry = json["mcpServers"]!.AsObject()["knowledge"]!.AsObject();
        entry["url"]!.GetValue<string>().ShouldBe("https://rag.afonsoft.dev/mcp");

        var status = await client.GetAsync("/api/mcp/status");
        status.StatusCode.ShouldBe(HttpStatusCode.OK);
        var raw = await status.Content.ReadAsStringAsync();
        var body = JsonNode.Parse(raw)!.AsObject();
        body["configuredUrl"]!.GetValue<string>().ShouldBe("https://rag.afonsoft.dev/mcp");
        // The API key is never returned.
        raw.ShouldNotContain("aft_integration_test_key");
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_PostMcpSync_Entao_202()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsync("/api/mcp/sync", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }
}
