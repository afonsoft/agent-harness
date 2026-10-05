using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Mcp;
using Taskboard.Integrations.Mcp;
using Xunit;

namespace Taskboard.Tests.Unit.Integrations.Mcp;

/// <summary>
/// SPEC-20261010-mcp-skills-hub RF-003: escrita de um MCP arbitrário nos
/// configs de user-scope dos CLIs (estilos JSON/TOML) — agents CLI-managed
/// (Antigravity/Cline/Continue) ficam Skipped nesta fase.
/// </summary>
public sealed class AgentMcpProvisioningServiceTests : IDisposable
{
    private readonly string _home = Path.Combine(
        Path.GetTempPath(), "agent-mcp-" + Guid.NewGuid().ToString("N"));

    private AgentMcpProvisioningService Service() =>
        new(_home, NullLogger<AgentMcpProvisioningService>.Instance);

    private static readonly ChatMcpServerSpec HttpSpec =
        new("kb", Url: "https://mcp.example.com/api",
            Headers: new Dictionary<string, string> { ["Authorization"] = "Bearer t" });

    private static readonly ChatMcpServerSpec StdioSpec =
        new("fs", Command: "npx", Args: ["-y", "@mcp/fs"]);

    [Fact]
    public async Task Dado_SpecHttp_Quando_ApplyEmClaude_Entao_ConfiguradoComUrl()
    {
        var results = await Service().ApplyAsync(
            HttpSpec, [AgentType.Claude], remove: false, CancellationToken.None);

        results.Single().State.ShouldBe(McpAgentState.Configured);

        var root = JsonNode.Parse(File.ReadAllText(Path.Join(_home, ".claude.json")))!.AsObject();
        var entry = root["mcpServers"]!["kb"]!.AsObject();
        entry["url"]!.GetValue<string>().ShouldBe("https://mcp.example.com/api");
        entry["type"]!.GetValue<string>().ShouldBe("http");
        entry["headers"]!["Authorization"]!.GetValue<string>().ShouldBe("Bearer t");
    }

    [Fact]
    public async Task Dado_SpecStdio_Quando_ApplyEmCodex_Entao_TomlComCommand()
    {
        var results = await Service().ApplyAsync(
            StdioSpec, [AgentType.Codex], remove: false, CancellationToken.None);

        results.Single().State.ShouldBe(McpAgentState.Configured);

        var toml = File.ReadAllText(Path.Join(_home, ".codex", "config.toml"));
        toml.ShouldContain("[mcp_servers.fs]");
        toml.ShouldContain("command = \"npx\"");
    }

    [Fact]
    public async Task Dado_SpecRepetida_Quando_Apply_Entao_IdempotenteConfigured()
    {
        var service = Service();
        await service.ApplyAsync(HttpSpec, [AgentType.Kimi], remove: false, CancellationToken.None);
        var again = await service.ApplyAsync(
            HttpSpec, [AgentType.Kimi], remove: false, CancellationToken.None);

        again.Single().State.ShouldBe(McpAgentState.Configured);
    }

    [Fact]
    public async Task Dado_SpecExistente_Quando_Remove_Entao_RemovidoDepoisNotConfigured()
    {
        var service = Service();
        await service.ApplyAsync(HttpSpec, [AgentType.Devin], remove: false, CancellationToken.None);

        var removed = await service.ApplyAsync(
            new ChatMcpServerSpec("kb"), [AgentType.Devin], remove: true, CancellationToken.None);
        removed.Single().State.ShouldBe(McpAgentState.Removed);

        var again = await service.ApplyAsync(
            new ChatMcpServerSpec("kb"), [AgentType.Devin], remove: true, CancellationToken.None);
        again.Single().State.ShouldBe(McpAgentState.NotConfigured);
    }

    [Fact]
    public async Task Dado_AgenteCliManaged_Quando_Apply_Entao_Skipped()
    {
        var results = await Service().ApplyAsync(
            HttpSpec, [AgentType.Antigravity, AgentType.Cline, AgentType.Continue],
            remove: false, CancellationToken.None);

        results.ShouldAllBe(r => r.State == McpAgentState.Skipped);
    }

    [Fact]
    public async Task Dado_AgenteSemTarget_Quando_Apply_Entao_Skipped()
    {
        var results = await Service().ApplyAsync(
            HttpSpec, [AgentType.Aider], remove: false, CancellationToken.None);

        results.Single().State.ShouldBe(McpAgentState.Skipped);
    }

    [Fact]
    public void Dado_Agente_Quando_IsWritable_Entao_CondizComEstilo()
    {
        AgentMcpProvisioningService.IsWritable(AgentType.Claude).ShouldBeTrue();
        AgentMcpProvisioningService.IsWritable(AgentType.Codex).ShouldBeTrue();
        AgentMcpProvisioningService.IsWritable(AgentType.Antigravity).ShouldBeFalse();
        AgentMcpProvisioningService.IsWritable(AgentType.Continue).ShouldBeFalse();
        AgentMcpProvisioningService.IsWritable(AgentType.Aider).ShouldBeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }
}
