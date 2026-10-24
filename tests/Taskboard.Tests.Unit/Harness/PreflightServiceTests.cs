using Microsoft.Extensions.Configuration;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Application.Contracts.Mcp;
using Taskboard.Application.Harness;
using Taskboard.Domain.Entities.Chat;
using Taskboard.Repositories;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

/// <summary>
/// SPEC-20261023 RF-C01: doctor de preflight — checks por CLI/MCP/provider/
/// worktree/disk agregados em um veredito ready|warning|blocked, sem side
/// effects além das próprias sondas.
/// </summary>
public class PreflightServiceTests
{
    private static AgentCliStatus CliOk(string binary = "claude") =>
        new(AgentCliKind.Claude, "Claude", binary, true, "1.2.3",
            AgentCliAuthStatus.Authenticated, "~", "claude login", "install-claude", "node", true);

    private static PreflightService NewService(
        IReadOnlyList<AgentCliStatus>? statuses = null,
        IReadOnlyList<ChatProvider>? providers = null,
        IReadOnlyList<ChatMcpServerStatus>? mcpServers = null,
        Dictionary<string, string?>? config = null,
        Exception? cliStatusError = null)
    {
        var cliStatus = Substitute.For<IAgentCliStatusService>();
        if (cliStatusError is not null)
        {
            cliStatus.GetStatusAsync(Arg.Any<CancellationToken>()).Returns<Task<IReadOnlyList<AgentCliStatus>>>(
                _ => throw cliStatusError);
        }
        else
        {
            cliStatus.GetStatusAsync(Arg.Any<CancellationToken>())
                .Returns(statuses ?? [CliOk()]);
        }

        var repo = Substitute.For<IRepository<ChatProvider>>();
        repo.ListAsync(Arg.Any<CancellationToken>())
            .Returns(providers ?? [ChatProvider.Create("p", "https://api.test", "sk-key", DateTime.UtcNow)]);

        var mcp = Substitute.For<IMcpClientManager>();
        mcp.GetServers().Returns(mcpServers ?? []);
        mcp.GetToolsAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<IChatTool>());

        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(config ?? new Dictionary<string, string?>
            {
                // Worktree root inside a writable temp dir — never the real ~/repos.
                ["Taskboard:WorktreeRoot"] = Path.Join(Path.GetTempPath(), $"tb-preflight-{Guid.NewGuid():N}"),
            })
            .Build();

        return new PreflightService(cliStatus, repo, mcp, cfg);
    }

    [Fact]
    public async Task Dado_TudoOk_Quando_Preflight_Entao_Ready()
    {
        var report = await NewService().RunAsync();

        report.Verdict.ShouldBe("ready");
        report.Checks.ShouldNotBeEmpty();
        report.Checks.ShouldAllBe(c => c.Status == PreflightCheckStatus.Ok);
    }

    [Fact]
    public async Task Dado_CliNaoAutenticado_Quando_Preflight_Entao_WarningComLogin()
    {
        var report = await NewService(statuses:
        [
            new AgentCliStatus(AgentCliKind.Claude, "Claude", "claude", true, "1.0",
                AgentCliAuthStatus.NotAuthenticated, "~", "claude login", "install-claude", "node", true),
        ]).RunAsync();

        report.Verdict.ShouldBe("warning");
        var check = report.Checks.Single(c => c.Name == "cli:claude");
        check.Status.ShouldBe(PreflightCheckStatus.Warning);
        check.NextAction.ShouldBe("claude login");
    }

    [Fact]
    public async Task Dado_NenhumCliInstalado_Quando_Preflight_Entao_Blocked()
    {
        var report = await NewService(statuses:
        [
            new AgentCliStatus(AgentCliKind.Claude, "Claude", "claude", false, null,
                AgentCliAuthStatus.Unknown, "~", "claude login", "install-claude", "node", false),
        ]).RunAsync();

        report.Verdict.ShouldBe("blocked");
        report.Checks.ShouldContain(c => c.Name == "agent-cli" && c.Status == PreflightCheckStatus.Blocked);
    }

    [Fact]
    public async Task Dado_SemProviderKey_Quando_Preflight_Entao_BlockedComNextAction()
    {
        var report = await NewService(providers: []).RunAsync();

        var check = report.Checks.Single(c => c.Name == "chat-provider");
        check.Status.ShouldBe(PreflightCheckStatus.Blocked);
        check.NextAction.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Dado_ProviderKeyViaEnv_Quando_Preflight_Entao_ProviderOk()
    {
        var report = await NewService(
            providers: [],
            config: new Dictionary<string, string?>
            {
                ["Taskboard:WorktreeRoot"] = Path.Join(Path.GetTempPath(), $"tb-preflight-{Guid.NewGuid():N}"),
                ["ANTHROPIC_API_KEY"] = "sk-ant-test",
            }).RunAsync();

        report.Checks.ShouldContain(c => c.Name == "chat-provider" && c.Status == PreflightCheckStatus.Ok);
    }

    [Fact]
    public async Task Dado_McpDoente_Quando_Preflight_Entao_Warning()
    {
        var report = await NewService(mcpServers:
        [
            new ChatMcpServerStatus("ctx7", "http", false, 0, "connect refused"),
        ]).RunAsync();

        report.Verdict.ShouldBe("warning");
        report.Checks.ShouldContain(c => c.Name == "mcp" && c.Status == PreflightCheckStatus.Warning);
    }

    [Fact]
    public async Task Dado_ProbeLancaExcecao_Quando_Preflight_Entao_WarningSemExplodir()
    {
        var report = await NewService(cliStatusError: new InvalidOperationException("boom")).RunAsync();

        report.Checks.ShouldContain(c =>
            c.Name == "agent-cli" && c.Status == PreflightCheckStatus.Warning);
        report.Verdict.ShouldBe("warning");
    }

    [Fact]
    public async Task Dado_WorktreeRootGravavel_Quando_Preflight_Entao_CheckOk()
    {
        var report = await NewService().RunAsync();

        report.Checks.ShouldContain(c => c.Name == "worktree-root" && c.Status == PreflightCheckStatus.Ok);
    }
}
