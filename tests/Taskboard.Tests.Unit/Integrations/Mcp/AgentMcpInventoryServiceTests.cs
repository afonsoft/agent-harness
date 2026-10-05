using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Integrations.Mcp;
using Xunit;

namespace Taskboard.Tests.Unit.Integrations.Mcp;

/// <summary>
/// SPEC-20261010-mcp-skills-hub RF-004: inventário read-only dos MCPs em cada
/// config de agent CLI — arquivo ausente/corrompido degrada para lista vazia.
/// </summary>
public sealed class AgentMcpInventoryServiceTests : IDisposable
{
    private readonly string _home = Path.Combine(
        Path.GetTempPath(), "agent-inv-" + Guid.NewGuid().ToString("N"));

    private AgentMcpInventoryService Service() =>
        new(_home, NullLogger<AgentMcpInventoryService>.Instance);

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_home, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Dado_SemConfigs_Quando_ListAll_Entao_AgentesComListaVazia()
    {
        var inventory = Service().ListAll();

        inventory.Count.ShouldBeGreaterThan(5);
        inventory.ShouldAllBe(i => i.Servers.Count == 0);
    }

    [Fact]
    public void Dado_ClaudeJson_Quando_Read_Entao_ServidoresComTransporte()
    {
        Write(".claude.json", """
            {
              "mcpServers": {
                "web": { "type": "http", "url": "https://mcp.example.com" },
                "fs": { "command": "npx", "args": ["-y", "@mcp/fs"] }
              }
            }
            """);

        var claude = Service().Read(AgentType.Claude)!;

        claude.Writable.ShouldBeTrue();
        claude.Servers.Count.ShouldBe(2);
        claude.Servers.Single(s => s.Name == "web").Transport.ShouldBe("http");
        claude.Servers.Single(s => s.Name == "web").Detail.ShouldContain("https://mcp.example.com");
        claude.Servers.Single(s => s.Name == "fs").Transport.ShouldBe("stdio");
        claude.Servers.Single(s => s.Name == "fs").Detail.ShouldContain("npx -y @mcp/fs");
    }

    [Fact]
    public void Dado_CodexToml_Quando_Read_Entao_ServidoresParseados()
    {
        Write(".codex/config.toml", """
            [mcp_servers.kb]
            url = "https://kb.example.com"

            [mcp_servers.kb.headers]
            Authorization = "Bearer t"
            """);

        var codex = Service().Read(AgentType.Codex)!;

        codex.Servers.Single().Name.ShouldBe("kb");
        codex.Servers[0].Transport.ShouldBe("http");
        codex.Servers[0].Detail.ShouldBe("https://kb.example.com");
    }

    [Fact]
    public void Dado_JsonCorrompido_Quando_Read_Entao_ListaVaziaSemThrow()
    {
        Write(".claude.json", "{ not json");

        var claude = Service().Read(AgentType.Claude)!;

        claude.Servers.ShouldBeEmpty();
    }

    [Fact]
    public void Dado_ContinueDirectory_Quando_Read_Entao_ArquivosJsonListados()
    {
        Write(".continue/mcpServers/a.json", """{ "mcpServers": { "one": { "url": "https://1" } } }""");
        Write(".continue/mcpServers/b.json", """{ "mcpServers": { "two": { "command": "c" } } }""");

        var continueAgent = Service().Read(AgentType.Continue)!;

        continueAgent.Writable.ShouldBeFalse();
        continueAgent.Servers.Count.ShouldBe(2);
    }

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }
}
