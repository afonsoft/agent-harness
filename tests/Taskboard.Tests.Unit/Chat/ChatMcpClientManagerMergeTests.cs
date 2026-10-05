using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shouldly;
using Taskboard.Application.Contracts.Mcp;
using Taskboard.Integrations.Mcp;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261010-mcp-skills-hub RF-002: merge <b>config &gt; ~/.agents &gt; rag</b>
/// (primeiro nome vence) e tag de <see cref="ChatMcpServerSpec.Origin"/>.
/// </summary>
public class ChatMcpClientManagerMergeTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static ChatMcpClientManager Manager(
        IConfiguration config, Func<GlobalAgentsMcpLoad>? globalLoader = null) =>
        new(config, LoggerFactory.Create(_ => { }), redactor: null,
            transportFactory: null, globalLoader: globalLoader);

    [Fact]
    public async Task Dado_ConfigEGlobalComMesmoNome_Quando_LoadSpecs_Entao_ConfigVence()
    {
        await using var manager = Manager(
            Config(new()
            {
                [ChatMcpClientManager.ServersKey] =
                    """[{"name":"fs","url":"https://config"}]""",
            }),
            globalLoader: () => new GlobalAgentsMcpLoad(
                [new ChatMcpServerSpec("fs", Url: "https://global", Origin: "agents-global"),
                 new ChatMcpServerSpec("extra", Command: "x", Origin: "agents-global")],
                "fp"));

        var specs = manager.LoadSpecs();

        specs.Count.ShouldBe(2);
        var fs = specs.Single(s => s.Name == "fs");
        fs.Url.ShouldBe("https://config");
        fs.Origin.ShouldBe("config");
        specs.Single(s => s.Name == "extra").Origin.ShouldBe("agents-global");
    }

    [Fact]
    public async Task Dado_GlobalERagComMesmoNome_Quando_LoadSpecs_Entao_GlobalVence()
    {
        await using var manager = Manager(
            Config(new()
            {
                ["Taskboard:Rag:Url"] = "https://rag",
                ["Taskboard:Rag:ServerName"] = "knowledge",
            }),
            globalLoader: () => new GlobalAgentsMcpLoad(
                [new ChatMcpServerSpec("knowledge", Url: "https://global", Origin: "agents-global")],
                "fp"));

        var specs = manager.LoadSpecs();

        specs.Single().Url.ShouldBe("https://global");
        specs[0].Origin.ShouldBe("agents-global");
    }

    [Fact]
    public async Task Dado_RagSemConflito_Quando_LoadSpecs_Entao_RagComOriginProprio()
    {
        await using var manager = Manager(
            Config(new() { ["Taskboard:Rag:Url"] = "https://rag" }));

        var specs = manager.LoadSpecs();

        specs.Single().Name.ShouldBe("knowledge");
        specs[0].Origin.ShouldBe("rag");
    }

    [Fact]
    public async Task Dado_IncludeGlobalAgentsFalse_Quando_LoadSpecs_Entao_GlobalIgnorado()
    {
        await using var manager = Manager(
            Config(new() { [ChatMcpClientManager.IncludeGlobalAgentsKey] = "false" }),
            globalLoader: () => new GlobalAgentsMcpLoad(
                [new ChatMcpServerSpec("g", Url: "https://g", Origin: "agents-global")], "fp"));

        manager.LoadSpecs().ShouldBeEmpty();
    }
}
