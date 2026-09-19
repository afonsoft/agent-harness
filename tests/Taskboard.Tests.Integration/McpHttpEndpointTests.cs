using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Taskboard.Mcp.Services;
using Taskboard.Server;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260919-mcp-v2-http-transport AC-1..AC-3: the Streamable HTTP MCP
/// endpoint lives under the authenticated /api group — anonymous gets 401,
/// authenticated clients can tools/list and tools/call over JSON-RPC.
/// </summary>
public class McpHttpEndpointTests : IClassFixture<McpHttpEndpointTests.McpFactory>
{
    private const string AdminPassword = "itest-admin-pass";

    private readonly McpFactory _factory;
    private readonly HttpClient _client;

    public McpHttpEndpointTests(McpFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public sealed class McpFactory : WebApplicationFactory<Program>
    {
        private HttpClient? _authedClient;

        public string DataDir { get; } = Path.Combine(Path.GetTempPath(), $"tb-itest-{Guid.NewGuid()}");
        public string HomeDir { get; } = Path.Combine(Path.GetTempPath(), $"tb-itest-home-{Guid.NewGuid()}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            foreach (var name in new[]
            {
                "TASKBOARD_ADMIN_USERNAME", "TASKBOARD_ADMIN_PASSWORD",
                "TASKBOARD_DATA_DIR", "Taskboard__DataDir", "TASKBOARD_API_KEY",
            })
            {
                Environment.SetEnvironmentVariable(name, null);
            }

            builder.UseSetting("Taskboard:DataDir", DataDir);
            builder.UseSetting("Taskboard:HomeDir", HomeDir);
            builder.UseSetting("Admin:Password", AdminPassword);
            builder.UseSetting("Taskboard:Skills:SyncOnStartup", "false");

            // The loopback client cannot reach the in-memory TestServer —
            // replace it with a deterministic fake (last registration wins).
            builder.ConfigureTestServices(services =>
                services.AddSingleton<ITaskboardApiClient>(new FakeTaskboardApiClient()));
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

    private sealed class FakeTaskboardApiClient : ITaskboardApiClient
    {
        public Task<JsonNode?> GetAsync(string path, CancellationToken ct = default)
            => Task.FromResult<JsonNode?>(JsonNode.Parse("""{"status":"ok","source":"fake-loopback"}"""));

        public Task<JsonNode?> PostAsync(string path, object? payload, CancellationToken ct = default)
            => Task.FromResult<JsonNode?>(JsonNode.Parse("""{"status":"ok","source":"fake-loopback"}"""));

        public Task<JsonNode?> PutAsync(string path, object? payload, CancellationToken ct = default)
            => Task.FromResult<JsonNode?>(JsonNode.Parse("""{"status":"ok","source":"fake-loopback"}"""));

        public Task<JsonNode?> PatchAsync(string path, object? payload, CancellationToken ct = default)
            => Task.FromResult<JsonNode?>(JsonNode.Parse("""{"status":"ok","source":"fake-loopback"}"""));

        public Task DeleteAsync(string path, CancellationToken ct = default) => Task.CompletedTask;

        public Task<JsonNode?> PostMultipartAsync(string path, MultipartFormDataContent content, CancellationToken ct = default)
            => Task.FromResult<JsonNode?>(JsonNode.Parse("""{"status":"ok","source":"fake-loopback"}"""));
    }

    private static HttpRequestMessage JsonRpc(string method, object? parameters = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/mcp");
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Content = new StringContent(
            $$"""{"jsonrpc":"2.0","id":1,"method":"{{method}}"{{(parameters is null ? "" : ",\"params\":" + System.Text.Json.JsonSerializer.Serialize(parameters))}}}""",
            Encoding.UTF8,
            "application/json");
        return request;
    }

    [Fact]
    public async Task Dado_SemAutenticacao_Quando_PostMcp_Entao_401()
    {
        var response = await _client.SendAsync(JsonRpc("tools/list"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_ToolsList_Entao_RetornaToolsRegistradas()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.SendAsync(JsonRpc("tools/list"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("get_issue_history");
        body.ShouldContain("list_github_issue_comments");
        body.ShouldContain("add_github_issue_comment");
        body.ShouldContain("cloud_status");
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_ToolsCallCloudStatus_Entao_ResultadoValido()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.SendAsync(JsonRpc(
            "tools/call",
            new { name = "cloud_status", arguments = new { } }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("fake-loopback");
    }
}
