using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.AiChat;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Domain.Entities;
using Taskboard.Dtos;
using Taskboard.Repositories;
using Taskboard.Server.Services;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.Server;

/// <summary>
/// SPEC-20260928-ai-code-generic-cli RF-003/RF-004: resolution of the argv a
/// terminal thread must spawn — builtin binary, custom def template or
/// docker exec wrap.
/// </summary>
public class ThreadPtyResolverTests
{
    private static AiChatThread PtyThread(string workspace, AgentType? type = null, string? container = null)
    {
        var thread = AiChatThread.CreateAgentThread(
            AiChatThreadId.NewGuid(), "t", ModelRef.From("default"), "medium",
            Sandbox.ReadOnly, type, workspacePath: workspace);
        thread.ConfigureCli("pty", container, agentCliId: null);
        return thread;
    }

    private static (ThreadPtyResolver Resolver, IRepository<AiChatThread> Threads,
        IAgentCliDefinitionRepository Defs, IAgentDiscoveryService Discovery) Create(
            AiChatThread? thread = null)
    {
        var threadRepo = Substitute.For<IRepository<AiChatThread>>();
        threadRepo.GetAsync(Arg.Any<AiChatThreadId>(), Arg.Any<CancellationToken>())
            .Returns(thread);

        var defs = Substitute.For<IAgentCliDefinitionRepository>();
        var discovery = Substitute.For<IAgentDiscoveryService>();
        var eligibility = Substitute.For<IAgentEligibilityService>();
        eligibility.GetEligibleTypesAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<AgentType>());

        var service = new AiChatService(
            threadRepo,
            Substitute.For<IRepository<AiChatRun>>(),
            Substitute.For<IRepository<AiChatEvent>>(),
            Substitute.For<ILLMProvider>(),
            Substitute.For<IThreadEventStreamService>(),
            Substitute.For<IServiceScopeFactory>(),
            eligibility,
            Substitute.For<ICliChatRunner>(),
            new ConfigurationBuilder().Build(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<AiChatService>>(),
            Substitute.For<Taskboard.Application.Contracts.Workspace.IWorkspacePathResolver>(),
            Substitute.For<IAgentModelConfigService>(),
            Substitute.For<IAgentModelCatalogService>(),
            defs,
            discovery);

        return (new ThreadPtyResolver(service, defs, discovery), threadRepo, defs, discovery);
    }

    [Fact]
    public async Task Dado_ThreadInexistente_Quando_Resolve_Entao_Erro()
    {
        var (sut, _, _, _) = Create(thread: null);

        var (result, error) = await sut.ResolveAsync("missing");

        result.ShouldBeNull();
        error.ShouldBe("Thread not found.");
    }

    [Fact]
    public async Task Dado_ThreadAcp_Quando_Resolve_Entao_ErroNaoPty()
    {
        var thread = AiChatThread.CreateAgentThread(
            AiChatThreadId.NewGuid(), "t", ModelRef.From("default"), "medium",
            Sandbox.ReadOnly, AgentType.Claude);
        var (sut, _, _, _) = Create(thread);

        var (result, error) = await sut.ResolveAsync(thread.Id.Value);

        result.ShouldBeNull();
        error.ShouldBe("Thread is not a terminal (pty) thread.");
    }

    [Fact]
    public async Task Dado_ThreadBuiltinPty_Quando_Resolve_Entao_ArgvBinarioResolvido()
    {
        var thread = PtyThread("/work/repo", AgentType.Aider);
        var (sut, _, _, discovery) = Create(thread);
        discovery.ResolveExecutablePath(AgentType.Aider).Returns("/usr/bin/aider");

        var (result, error) = await sut.ResolveAsync(thread.Id.Value);

        error.ShouldBeNull();
        result.ShouldNotBeNull();
        result!.WorkingDirectory.ShouldBe("/work/repo");
        result.Command.ShouldBe(["/usr/bin/aider"]);
    }

    [Fact]
    public async Task Dado_ThreadCustomCli_Quando_Resolve_Entao_ArgvDoTemplate()
    {
        var thread = AiChatThread.CreateAgentThread(
            AiChatThreadId.NewGuid(), "t", ModelRef.From("sonnet"), "medium",
            Sandbox.ReadOnly, agentType: null, workspacePath: "/work");
        thread.ConfigureCli("pty", null, agentCliId: "custom-minha");
        var (sut, _, defs, _) = Create(thread);
        defs.GetAsync("custom-minha", Arg.Any<CancellationToken>()).Returns(
            new AgentCliDefinitionDto(
                "custom-minha", "Minha", "minha-cli", "--fast {model}", "pty",
                "--model", "--version", true, true));

        var (result, error) = await sut.ResolveAsync(thread.Id.Value);

        error.ShouldBeNull();
        result.ShouldNotBeNull();
        result!.Command.ShouldBe(["minha-cli", "--fast", "sonnet"]);
    }

    [Fact]
    public async Task Dado_ThreadCustomCliDesabilitada_Quando_Resolve_Entao_Erro()
    {
        var thread = AiChatThread.CreateAgentThread(
            AiChatThreadId.NewGuid(), "t", ModelRef.From("default"), "medium",
            Sandbox.ReadOnly, agentType: null);
        thread.ConfigureCli("pty", null, agentCliId: "custom-off");
        var (sut, _, defs, _) = Create(thread);
        defs.GetAsync("custom-off", Arg.Any<CancellationToken>()).Returns(
            new AgentCliDefinitionDto(
                "custom-off", "Off", "off-cli", "", "pty", null, "--version", false, true));

        var (result, error) = await sut.ResolveAsync(thread.Id.Value);

        result.ShouldBeNull();
        (error ?? string.Empty).ShouldContain("disabled");
    }

    [Fact]
    public async Task Dado_ContextoDocker_Quando_Resolve_Entao_DockerExecWrap()
    {
        var thread = PtyThread("/work", AgentType.Claude, container: "dev");
        var (sut, _, _, discovery) = Create(thread);
        discovery.ResolveExecutablePath(AgentType.Claude).Returns("/usr/bin/claude");

        var (result, error) = await sut.ResolveAsync(thread.Id.Value);

        error.ShouldBeNull();
        result!.Command.ShouldBe(["docker", "exec", "-it", "dev", "/usr/bin/claude"]);
    }

    [Fact]
    public async Task Dado_ContainerInvalido_Quando_Resolve_Entao_Erro()
    {
        var thread = PtyThread("/work", AgentType.Claude, container: "a;b");
        var (sut, _, _, discovery) = Create(thread);
        discovery.ResolveExecutablePath(AgentType.Claude).Returns("/usr/bin/claude");

        var (result, error) = await sut.ResolveAsync(thread.Id.Value);

        result.ShouldBeNull();
        (error ?? string.Empty).ShouldContain("Invalid container name");
    }

    [Fact]
    public void Dado_ThreadId_Quando_SessionIdFor_Entao_PrefixoT() =>
        ThreadPtyResolver.SessionIdFor("abc").ShouldBe("t-abc");
}
