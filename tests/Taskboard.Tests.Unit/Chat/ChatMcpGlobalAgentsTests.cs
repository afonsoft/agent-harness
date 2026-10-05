using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Shouldly;
using Taskboard.Application.Contracts.Mcp;
using Taskboard.Integrations.Mcp;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261010-mcp-skills-hub RF-002: o status de cada servidor carrega a
/// origem (<c>agents-global</c>) e uma mudança no conteúdo/fingerprint do
/// ~/.agents dispara reconexão sem FileSystemWatcher.
/// </summary>
public class ChatMcpGlobalAgentsTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static IClientTransport ThrowingTransport(ChatMcpServerSpec spec) =>
        throw new InvalidOperationException("fake transport");

    [Fact]
    public async Task Dado_ServidorGlobal_Quando_ConectaFalha_Entao_StatusComOriginGlobal()
    {
        await using var manager = new ChatMcpClientManager(
            Config(new() { [ChatMcpClientManager.EnabledKey] = "true" }),
            LoggerFactory.Create(_ => { }),
            redactor: null,
            transportFactory: ThrowingTransport,
            globalLoader: () => new GlobalAgentsMcpLoad(
                [new ChatMcpServerSpec("g", Url: "http://localhost:1", Origin: "agents-global")],
                "fp"));

        await manager.GetToolsAsync(CancellationToken.None);

        var status = manager.GetServers().Single();
        status.Name.ShouldBe("g");
        status.Origin.ShouldBe("agents-global");
        status.Healthy.ShouldBeFalse();
        status.Error.ShouldNotBeNull().ShouldContain("fake transport");
    }

    [Fact]
    public async Task Dado_FingerprintMudou_Quando_GetToolsNovamente_Entao_ReconectaNovoSet()
    {
        var current = new GlobalAgentsMcpLoad(
            [new ChatMcpServerSpec("g1", Url: "http://localhost:1", Origin: "agents-global")],
            "fp1");
        await using var manager = new ChatMcpClientManager(
            Config(new() { [ChatMcpClientManager.EnabledKey] = "true" }),
            LoggerFactory.Create(_ => { }),
            redactor: null,
            transportFactory: ThrowingTransport,
            globalLoader: () => current);

        await manager.GetToolsAsync(CancellationToken.None);
        manager.GetServers().Single().Name.ShouldBe("g1");

        current = new GlobalAgentsMcpLoad(
            [new ChatMcpServerSpec("g2", Url: "http://localhost:1", Origin: "agents-global")],
            "fp2");
        await manager.GetToolsAsync(CancellationToken.None);

        manager.GetServers().Single().Name.ShouldBe("g2");
    }

    [Fact]
    public async Task Dado_FingerprintIgual_Quando_GetToolsNovamente_Entao_NaoReconecta()
    {
        var connects = 0;
        var load = new GlobalAgentsMcpLoad(
            [new ChatMcpServerSpec("g", Url: "http://localhost:1", Origin: "agents-global")],
            "fp");
        await using var manager = new ChatMcpClientManager(
            Config(new() { [ChatMcpClientManager.EnabledKey] = "true" }),
            LoggerFactory.Create(_ => { }),
            redactor: null,
            transportFactory: spec => { connects++; return ThrowingTransport(spec); },
            globalLoader: () => load);

        await manager.GetToolsAsync(CancellationToken.None);
        await manager.GetToolsAsync(CancellationToken.None);

        connects.ShouldBe(1);
    }
}
