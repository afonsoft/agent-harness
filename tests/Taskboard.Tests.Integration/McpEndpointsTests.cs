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
            // Same scrub as TaskboardWebApplicationFactory: the developer shell may
            // carry the deployed server's env vars, which take precedence over
            // UseSetting and would break login/DataDir hermeticity.
            foreach (var name in new[]
            {
                "HARNESS_ADMIN_USERNAME", "HARNESS_ADMIN_PASSWORD",
                "TASKBOARD_ADMIN_USERNAME", "TASKBOARD_ADMIN_PASSWORD",
                "HARNESS_DATA_DIR", "TASKBOARD_DATA_DIR", "Harness__DataDir", "Taskboard__DataDir"
            })
            {
                Environment.SetEnvironmentVariable(name, null);
            }

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

        // Provision runs in background — wait on the observable run status
        // (GET /api/mcp/status) instead of polling the file on a fixed deadline.
        var claudeConfig = Path.Combine(_factory.HomeDir, ".claude.json");
        await WaitForClaudeAgentStateAsync(client, McpAgentStateConfigured);

        HasManagedEntry(claudeConfig).ShouldBeTrue();
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
    public async Task Dado_SemUrlSalva_Quando_PostMcpSync_Entao_400SemTocarArquivos()
    {
        // Covers SPEC-20260918-rag-mcp-sync RF-001/AC1: Sync without a saved URL is
        // rejected — it must never fall through to the implicit remove mode.
        var client = await CreateAuthenticatedClientAsync();
        // Determinism: a previous test may have saved a URL — clear any override.
        await client.DeleteAsync("/api/configuration/Taskboard:Rag:Url");

        var response = await client.PostAsync("/api/mcp/sync", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        body["error"]!["code"]!.GetValue<string>().ShouldBe("rag-not-configured");

        // A rejected sync must not touch files: if a previous test provisioned
        // the entry, it has to still be there (remove-mode would have stripped it).
        var claudeConfig = Path.Combine(_factory.HomeDir, ".claude.json");
        if (File.Exists(claudeConfig))
        {
            JsonNode.Parse(File.ReadAllText(claudeConfig))!.AsObject()["mcpServers"]!
                .AsObject().ContainsKey("knowledge").ShouldBeTrue(
                    "a rejected sync must not remove the managed entry");
        }
    }

    [Fact]
    public async Task Dado_UrlSalva_Quando_PostMcpSync_Entao_202EEscreveArquivos()
    {
        // Covers AC2: with a saved URL, Sync provisions the agent config files.
        var client = await CreateAuthenticatedClientAsync();
        var put = await client.PutAsJsonAsync("/api/mcp/rag", new
        {
            name = "knowledge",
            url = "https://rag.afonsoft.dev/mcp",
            apiKey = (string?)null
        });
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await client.PostAsync("/api/mcp/sync", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var claudeConfig = Path.Combine(_factory.HomeDir, ".claude.json");
        await WaitForClaudeAgentStateAsync(client, McpAgentStateConfigured);

        HasManagedEntry(claudeConfig).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_UrlVazia_Quando_PutMcpRag_Entao_400()
    {
        // Covers RF-005: clearing the URL via PUT is rejected — removal is POST mcp/remove.
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync("/api/mcp/rag", new
        {
            name = "knowledge",
            url = "",
            apiKey = (string?)null
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        body["error"]!["code"]!.GetValue<string>().ShouldBe("rag-url-required");
    }

    [Fact]
    public async Task Dado_SemAutenticacao_Quando_PostMcpRemove_Entao_401()
    {
        var response = await _client.PostAsync("/api/mcp/remove", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_PostMcpRemove_Entao_202ERemoveEntry()
    {
        // Covers RF-002/AC3: explicit removal works regardless of the stored URL.
        var client = await CreateAuthenticatedClientAsync();
        await client.PutAsJsonAsync("/api/mcp/rag", new
        {
            name = "knowledge",
            url = "https://rag.afonsoft.dev/mcp",
            apiKey = (string?)null
        });
        var claudeConfig = Path.Combine(_factory.HomeDir, ".claude.json");
        await WaitForClaudeAgentStateAsync(client, McpAgentStateConfigured);

        var response = await client.PostAsync("/api/mcp/remove", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        // Removal is fire-and-forget (202 + queued background run). Wait on the
        // run status: the removal run replaces lastRun's per-agent states, so
        // the Claude agent flips Configured → Removed when it completes.
        await WaitForClaudeAgentStateAsync(
            client, McpAgentStateRemoved, McpAgentStateNotConfigured,
            McpAgentStateSkipped, McpAgentStateFailed);

        HasManagedEntry(claudeConfig).ShouldBeFalse(
            "the managed entry should be removed from .claude.json");
    }

    // McpAgentState / AgentType serialize as numbers (ApiJsonOptions has no
    // JsonStringEnumConverter): Claude = AgentType 1; Configured = 0,
    // Removed = 2, Skipped = 4, NotConfigured = 5, Failed = 6.
    private const int AgentTypeClaude = 1;
    private const int McpAgentStateConfigured = 0;
    private const int McpAgentStateRemoved = 2;
    private const int McpAgentStateSkipped = 4;
    private const int McpAgentStateNotConfigured = 5;
    private const int McpAgentStateFailed = 6;

    private static async Task WaitForClaudeAgentStateAsync(
        HttpClient client, params int[] acceptedStates)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var status = await client.GetAsync("/api/mcp/status");
            status.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = JsonNode.Parse(await status.Content.ReadAsStringAsync())!.AsObject();
            var agent = body["agents"]?.AsArray()
                .FirstOrDefault(a => a?["agent"]?.GetValue<int>() == AgentTypeClaude);
            if (agent is not null &&
                acceptedStates.Contains(agent["state"]!.GetValue<int>()))
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail(
            $"Claude agent state never reached [{string.Join(", ", acceptedStates)}] within 30s");
    }

    private static bool HasManagedEntry(string claudeConfigPath)
    {
        if (!File.Exists(claudeConfigPath))
        {
            return false;
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(claudeConfigPath))!.AsObject()["mcpServers"]!
                .AsObject().ContainsKey("knowledge");
        }
        catch
        {
            // File may be mid-atomic-write — treat as not-ready and keep polling.
            return false;
        }
    }
}
