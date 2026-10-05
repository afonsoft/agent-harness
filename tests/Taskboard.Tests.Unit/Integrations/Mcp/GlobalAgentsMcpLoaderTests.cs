using Shouldly;
using Taskboard.Integrations.Mcp;
using Xunit;

namespace Taskboard.Tests.Unit.Integrations.Mcp;

/// <summary>
/// SPEC-20261010-mcp-skills-hub RF-001: leitura dos MCPs globais de
/// <c>~/.agents</c> (<c>mcp.json</c>, <c>mcp_config.json</c>,
/// <c>mcps/**/*.json</c>) com fingerprint conteúdo+mtime.
/// </summary>
public sealed class GlobalAgentsMcpLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "agents-mcp-" + Guid.NewGuid().ToString("N"));

    private GlobalAgentsMcpLoader Loader() => new(_dir);

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Dado_DiretorioInexistente_Quando_Load_Entao_Vazio()
    {
        var load = Loader().Load();

        load.Specs.ShouldBeEmpty();
        load.Fingerprint.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Dado_McpJsonComUrlECommand_Quando_Load_Entao_SpecsComOriginGlobal()
    {
        Write("mcp.json", """
            {
              "mcpServers": {
                "fs": { "command": "npx", "args": ["-y", "@mcp/fs"], "env": { "KEY": "env:MCP_KEY" } },
                "web": { "url": "https://mcp.example.com/api", "headers": { "Authorization": "env:MCP_TOKEN" } }
              }
            }
            """);

        var load = Loader().Load();

        load.Specs.Count.ShouldBe(2);
        var fs = load.Specs.Single(s => s.Name == "fs");
        fs.Command.ShouldBe("npx");
        fs.Args.ShouldBe(["-y", "@mcp/fs"]);
        fs.Env.ShouldBe(new Dictionary<string, string> { ["KEY"] = "env:MCP_KEY" });
        fs.Origin.ShouldBe("agents-global");
        var web = load.Specs.Single(s => s.Name == "web");
        web.Url.ShouldBe("https://mcp.example.com/api");
        web.Headers.ShouldBe(new Dictionary<string, string> { ["Authorization"] = "env:MCP_TOKEN" });
    }

    [Fact]
    public void Dado_VariosArquivos_Quando_Load_Entao_UniaoSemDuplicarNome()
    {
        Write("mcp.json", """{ "mcpServers": { "shared": { "url": "https://a" } } }""");
        Write("mcp_config.json", """
            { "mcpServers": { "shared": { "url": "https://b" }, "extra": { "command": "run" } } }
            """);
        Write("mcps/nested/more.json", """{ "mcpServers": { "deep": { "url": "https://c" } } }""");

        var load = Loader().Load();

        load.Specs.Count.ShouldBe(3);
        // Primeiro arquivo ganha o nome duplicado (ordem determinística).
        load.Specs.Single(s => s.Name == "shared").Url.ShouldBe("https://a");
        load.Specs.Single(s => s.Name == "extra").Command.ShouldBe("run");
        load.Specs.Single(s => s.Name == "deep").Url.ShouldBe("https://c");
    }

    [Fact]
    public void Dado_EntradaSemUrlNemCommand_Quando_Load_Entao_Ignorada()
    {
        Write("mcp.json", """
            { "mcpServers": { "empty": { "args": [] }, "ok": { "command": "x" } } }
            """);

        var load = Loader().Load();

        load.Specs.Single().Name.ShouldBe("ok");
    }

    [Fact]
    public void Dado_ArquivoEditado_Quando_Load_Entao_FingerprintMuda()
    {
        var file = Write("mcp.json", """{ "mcpServers": { "a": { "url": "https://1" } } }""");
        var first = Loader().Load().Fingerprint;

        Thread.Sleep(20); // mtime tick
        File.WriteAllText(file, """{ "mcpServers": { "a": { "url": "https://2" } } }""");
        var second = Loader().Load().Fingerprint;

        first.ShouldNotBe(second);
    }

    [Fact]
    public void Dado_JsonInvalido_Quando_Load_Entao_VazioSemThrow()
    {
        Write("mcp.json", "{ not json");

        var load = Loader().Load();

        load.Specs.ShouldBeEmpty();
    }

    [Fact]
    public void Dado_ArgsComValoresNaoString_Quando_Load_Entao_Tolerados()
    {
        Write("mcp.json", """
            { "mcpServers": { "n": { "command": "c", "args": ["a", 1, true] } } }
            """);

        var load = Loader().Load();

        load.Specs.Single().Args.ShouldBe(["a"]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
