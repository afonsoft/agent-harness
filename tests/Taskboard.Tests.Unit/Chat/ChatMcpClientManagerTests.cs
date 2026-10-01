using System.Text.Json;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shouldly;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Mcp;
using Taskboard.Integrations.Mcp;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261001-chat-mcp-client FR-002/003/004: conexão lazy, descoberta de
/// tools com prefixo <c>mcp_</c>, chamada com timeout/retry/redação e
/// comportamento inerte quando o master está desligado. Usa um servidor MCP
/// stdio fake (script Python efêmero) — o mesmo caminho de produção.
/// </summary>
public class ChatMcpClientManagerTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static ChatToolContext Ctx() => new(
        WorkspacePath: "/tmp",
        ProviderId: Guid.NewGuid(),
        ProviderBaseUrl: "http://localhost",
        ProviderApiKey: "",
        ImageModel: "",
        SearchBackend: "none",
        SearchUrl: "",
        SearchApiKey: "");

    [Fact]
    public async Task Dado_McpDisabled_Quando_GetTools_Entao_ZeroToolsMcp()
    {
        await using var manager = new ChatMcpClientManager(
            Config(new() { [ChatMcpClientManager.EnabledKey] = "false" }),
            LoggerFactory.Create(_ => { }),
            redactor: null);

        (await manager.GetToolsAsync(CancellationToken.None)).ShouldBeEmpty();
        manager.GetServers().ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_ServerHabilitado_Quando_Lista_Entao_ToolsComPrefixoMcp()
    {
        using var server = new FakeMcpServer();
        await using var manager = new ChatMcpClientManager(
            Config(new()
            {
                [ChatMcpClientManager.EnabledKey] = "true",
                [ChatMcpClientManager.ServersKey] =
                    $$"""[{"name":"fake","command":"{{server.Command}}","args":{{JsonSerializer.Serialize(server.Args)}}}]""",
            }),
            LoggerFactory.Create(_ => { }),
            redactor: null);

        var tools = await manager.GetToolsAsync(CancellationToken.None);

        tools.Count.ShouldBe(1);
        tools[0].Name.ShouldBe("mcp_fake_echo");
        tools[0].CapabilityId.ShouldBe("mcp:fake/echo");
        tools[0].Kind.ShouldBe(ChatCapabilityKind.McpTool);
        tools[0].Origin.ShouldBe("fake");
        manager.GetServers().Single().Healthy.ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_ToolHabilitada_Quando_Calla_Entao_ResultadoTexto()
    {
        using var server = new FakeMcpServer();
        await using var manager = new ChatMcpClientManager(
            Config(new()
            {
                [ChatMcpClientManager.EnabledKey] = "true",
                [ChatMcpClientManager.ServersKey] =
                    $$"""[{"name":"fake","command":"{{server.Command}}","args":{{JsonSerializer.Serialize(server.Args)}}}]""",
            }),
            LoggerFactory.Create(_ => { }),
            redactor: null);

        await manager.GetToolsAsync(CancellationToken.None);
        var result = await manager.CallAsync(
            "fake", "echo",
            JsonDocument.Parse("""{"text":"hello"}""").RootElement,
            Ctx(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("echo:hello");
    }

    [Fact]
    public async Task Dado_ServerOffline_Quando_Calla_Entao_ResultErroSemExcecao()
    {
        await using var manager = new ChatMcpClientManager(
            Config(new()
            {
                [ChatMcpClientManager.EnabledKey] = "true",
                [ChatMcpClientManager.ServersKey] = """[{"name":"dead","command":"definitely-not-a-real-command-xyz"}]""",
            }),
            LoggerFactory.Create(_ => { }),
            redactor: null);

        (await manager.GetToolsAsync(CancellationToken.None)).ShouldBeEmpty();
        var status = manager.GetServers().Single();
        status.Healthy.ShouldBeFalse();
        status.Error.ShouldNotBeNull();

        var result = await manager.CallAsync(
            "dead", "anything", JsonDocument.Parse("{}").RootElement,
            Ctx(), CancellationToken.None);
        result.Refused.ShouldBeTrue();
        result.Json.ShouldContain("unavailable");
    }

    [Fact]
    public async Task Dado_RagServerConfigurado_Quando_LoadSpecs_Entao_Incluido()
    {
        await using var manager = new ChatMcpClientManager(
            Config(new()
            {
                [ChatMcpClientManager.EnabledKey] = "true",
                ["Taskboard:Rag:Url"] = "http://localhost:9999/mcp",
                ["Taskboard:Rag:ApiKey"] = "secret-key",
            }),
            LoggerFactory.Create(_ => { }),
            redactor: null);

        var specs = manager.LoadSpecs();

        specs.ShouldContain(s => s.Name == "knowledge" && s.Url == "http://localhost:9999/mcp");
        // A API key vira header resolvido server-side — nunca exposta no spec público.
        specs.Single(s => s.Name == "knowledge").Headers!["Authorization"].ShouldBe("Bearer secret-key");
    }

    [Fact]
    public async Task Dado_ConfigMudou_Quando_GetTools_Entao_ReconectaComNovosSpecs()
    {
        // B-08: _connectAttempted travava o manager na primeira configuração —
        // mudança de servidores nunca recarregava. O fingerprint reage ao reload.
        using var server = new FakeMcpServer();
        var source = new MutableConfigSource();
        source.Data[ChatMcpClientManager.EnabledKey] = "true";
        source.Data[ChatMcpClientManager.ServersKey] =
            $$"""[{"name":"fake","command":"{{server.Command}}","args":{{JsonSerializer.Serialize(server.Args)}}}]""";
        var config = new ConfigurationBuilder().Add(source).Build();

        await using var manager = new ChatMcpClientManager(
            config, LoggerFactory.Create(_ => { }), redactor: null);

        var tools = await manager.GetToolsAsync(CancellationToken.None);
        tools.Select(t => t.Name).ShouldBe(["mcp_fake_echo"]);

        // Hot-reload: mesmo binário, nome diferente → fingerprint muda.
        source.Data[ChatMcpClientManager.ServersKey] =
            $$"""[{"name":"fake2","command":"{{server.Command}}","args":{{JsonSerializer.Serialize(server.Args)}}}]""";
        ((IConfigurationRoot)config).Reload();

        var reloaded = await manager.GetToolsAsync(CancellationToken.None);

        reloaded.Select(t => t.Name).ShouldBe(["mcp_fake2_echo"]);
        manager.GetServers().Single().Name.ShouldBe("fake2");
    }

    /// <summary>Fonte de configuração mutável — simula o hot-reload do appsettings.</summary>
    private sealed class MutableConfigSource : IConfigurationSource
    {
        public Dictionary<string, string?> Data { get; } = new(StringComparer.Ordinal);

        public IConfigurationProvider Build(IConfigurationBuilder builder) => new Provider(Data);

        private sealed class Provider(Dictionary<string, string?> source) : ConfigurationProvider
        {
            public override void Load() => Data = new Dictionary<string, string?>(source, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Fake stdio MCP server (SPEC §10) — script Python efêmero falando NDJSON
    /// JSON-RPC (initialize/tools-list/tools-call) em stdin/stdout.
    /// </summary>
    private sealed class FakeMcpServer : IDisposable
    {
        public const string Script = """
            import sys, json
            for line in sys.stdin:
                msg = json.loads(line)
                if "id" not in msg:
                    continue
                m = msg["method"]
                if m == "initialize":
                    out = {"jsonrpc":"2.0","id":msg["id"],"result":{"protocolVersion":msg["params"]["protocolVersion"],"capabilities":{"tools":{}},"serverInfo":{"name":"fake","version":"1"}}}
                elif m == "tools/list":
                    out = {"jsonrpc":"2.0","id":msg["id"],"result":{"tools":[{"name":"echo","description":"Echo text","inputSchema":{"type":"object","properties":{"text":{"type":"string"}}}}]}}
                elif m == "tools/call":
                    out = {"jsonrpc":"2.0","id":msg["id"],"result":{"content":[{"type":"text","text":"echo:"+msg["params"]["arguments"]["text"]}],"isError":False}}
                elif m == "ping":
                    out = {"jsonrpc":"2.0","id":msg["id"],"result":{}}
                else:
                    out = {"jsonrpc":"2.0","id":msg["id"],"error":{"code":-32601,"message":"unknown"}}
                sys.stdout.write(json.dumps(out) + "\n")
                sys.stdout.flush()
            """;

        public string Command { get; } = "python3";

        public IReadOnlyList<string> Args { get; }

        public FakeMcpServer()
        {
            var path = Path.Combine(Path.GetTempPath(), $"fake-mcp-{Guid.NewGuid():N}.py");
            File.WriteAllText(path, Script);
            Args = [path];
        }

        public void Dispose()
        {
            try
            {
                File.Delete(Args[0]);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
