using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20261010-mcp-skills-hub RF-004: endpoints do hub MCP por agent CLI —
/// inventário, install (validação + escrita real no home fake) e remove.
/// </summary>
public class McpAgentsEndpointsTests : IClassFixture<McpAgentsEndpointsTests.McpFactory>
{
    private readonly McpFactory _factory;

    public McpAgentsEndpointsTests(McpFactory factory)
    {
        _factory = factory;
    }

    public sealed class McpFactory : TaskboardWebApplicationFactory
    {
        public string HomeDir { get; }

        public McpFactory()
        {
            HomeDir = Path.Combine(Path.GetTempPath(), $"tb-itest-mcp-{Guid.NewGuid():N}");
            HomeDirOverride = HomeDir;
        }
    }

    [Fact]
    public async Task Dado_Anonimo_Quando_GetAgents_Entao_401()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        (await client.GetAsync("/api/mcp/agents")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_GetAgents_Entao_InventarioComWritable()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var inventory = await client.GetFromJsonAsync<JsonArray>("/api/mcp/agents");

        inventory.ShouldNotBeNull();
        inventory.Count.ShouldBeGreaterThan(5);
        var claude = inventory.Cast<JsonObject>().Single(a => a["agent"]?.GetValue<int>() == 1 /* Claude */);
        claude["writable"]!.GetValue<bool>().ShouldBeTrue();
        claude["configPath"]!.GetValue<string>().ShouldContain(_factory.HomeDir);
    }

    [Fact]
    public async Task Dado_InstallValido_Quando_Post_Entao_EscreveClaudeJson()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/mcp/agents/install", new
        {
            name = "kb",
            url = "https://mcp.example.com/api",
            agents = new[] { 1 /* Claude */ }
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var results = await response.Content.ReadFromJsonAsync<JsonArray>();
        results!.Cast<JsonObject>().Single()["state"]!.GetValue<int>().ShouldBe(0); // Configured

        var claudePath = Path.Combine(_factory.HomeDir, ".claude.json");
        File.Exists(claudePath).ShouldBeTrue();
        File.ReadAllText(claudePath).ShouldContain("\"kb\"");
    }

    [Fact]
    public async Task Dado_InstallSemTransporte_Quando_Post_Entao_400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/mcp/agents/install", new
        {
            name = "bad",
            agents = new[] { 1 }
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_InstallSemAgentes_Quando_Post_Entao_400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/mcp/agents/install", new
        {
            name = "bad",
            url = "https://x",
            agents = Array.Empty<int>()
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_InstallAntigravity_Quando_Post_Entao_400_NaoEscrevivel()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/mcp/agents/install", new
        {
            name = "x",
            url = "https://x",
            agents = new[] { 5 /* Antigravity — CLI-managed */ }
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_Remove_Quando_Post_Entao_RemoveEntrada()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        await client.PostAsJsonAsync("/api/mcp/agents/install", new
        {
            name = "gone",
            command = "npx",
            args = new[] { "-y", "@mcp/fs" },
            agents = new[] { 1 }
        });

        var response = await client.PostAsJsonAsync("/api/mcp/agents/remove", new
        {
            name = "gone",
            agents = new[] { 1 }
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        File.ReadAllText(Path.Combine(_factory.HomeDir, ".claude.json"))
            .ShouldNotContain("\"gone\"");
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_GetChat_Entao_ListaDeServidores()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/mcp/chat");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonArray>()).ShouldNotBeNull();
    }
}
