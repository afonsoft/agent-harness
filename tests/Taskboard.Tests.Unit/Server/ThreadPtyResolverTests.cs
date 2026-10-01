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

    /// <summary>Runner fake que simula `docker info`/`ps`/`exec which`.</summary>
    private sealed class FakeRunner : Taskboard.Integrations.Skills.ISkillsInstallRunner
    {
        public string PsOutput { get; set; } = string.Empty;

        public Task<Taskboard.Integrations.Skills.CommandResult> RunAsync(
            string executable,
            string workingDirectory,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            var result = arguments switch
            {
                _ when arguments.Contains("info") => new Taskboard.Integrations.Skills.CommandResult(0, "27.0", string.Empty),
                _ when arguments.Contains("ps") => new Taskboard.Integrations.Skills.CommandResult(0, PsOutput, string.Empty),
                _ => new Taskboard.Integrations.Skills.CommandResult(0, "/usr/bin/cli", string.Empty),
            };
            return Task.FromResult(result);
        }
    }

    private static (ThreadPtyResolver Resolver, IRepository<AiChatThread> Threads,
        IAgentCliDefinitionRepository Defs, IAgentDiscoveryService Discovery) Create(
            AiChatThread? thread = null, string dockerPsOutput = "dev|ubuntu:24.04\n")
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
            Substitute.For<ILlmProvider>(),
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
            discovery,
            Substitute.For<IContainerCliDiscovery>());

        var docker = new Taskboard.Integrations.Agents.DockerCliDiscovery(
            Path.GetTempPath(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Taskboard.Integrations.Agents.DockerCliDiscovery>.Instance,
            executableLocator: _ => "/usr/bin/docker",
            runner: new FakeRunner { PsOutput = dockerPsOutput });
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "resolver-ws");
        var workspace = new Taskboard.Integrations.Workspace.WorkspaceService(
            workspaceRoot,
            Path.GetTempPath(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Taskboard.Integrations.Workspace.WorkspaceService>.Instance);

        return (new ThreadPtyResolver(service, defs, discovery, docker, workspace), threadRepo, defs, discovery);
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
    public async Task Dado_ContextoDocker_Quando_Resolve_Entao_DockerExecComNomeDoBinario()
    {
        // SPEC-20260929-docker-cli-context RF-001: dentro do contêiner o binário
        // é resolvido pelo PATH do contêiner — o path do host não vale lá.
        var thread = PtyThread("/work", AgentType.Claude, container: "dev");
        var (sut, _, _, discovery) = Create(thread);
        discovery.ResolveExecutablePath(AgentType.Claude).Returns("/home/harness/.local/bin/claude");

        var (result, error) = await sut.ResolveAsync(thread.Id.Value);

        error.ShouldBeNull();
        result!.Command.ShouldBe(["docker", "exec", "-it", "dev", "claude"]);
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

    // SPEC-20260929-pty-session-security RF-002: o nome persistido só vale
    // contra contêineres que a descoberta vê rodando — default deny.

    [Fact]
    public async Task Dado_ContainerForaDaDescoberta_Quando_Resolve_Entao_Erro()
    {
        var thread = PtyThread("/work", AgentType.Claude, container: "ghost");
        var (sut, _, _, discovery) = Create(thread, dockerPsOutput: "dev|ubuntu:24.04\n");
        discovery.ResolveExecutablePath(AgentType.Claude).Returns("/usr/bin/claude");

        var (result, error) = await sut.ResolveAsync(thread.Id.Value);

        result.ShouldBeNull();
        (error ?? string.Empty).ShouldContain("not running or not allowed");
    }

    [Fact]
    public async Task Dado_DockerIndisponivel_Quando_ResolveComContainer_Entao_Erro()
    {
        var thread = PtyThread("/work", AgentType.Claude, container: "dev");
        var (sut, _, _, discovery) = Create(thread, dockerPsOutput: string.Empty);
        discovery.ResolveExecutablePath(AgentType.Claude).Returns("/usr/bin/claude");

        var (result, error) = await sut.ResolveAsync(thread.Id.Value);

        result.ShouldBeNull();
        (error ?? string.Empty).ShouldContain("not running or not allowed");
    }

    // SPEC-20260929-pty-session-security RF-006: sem workspace explícito o
    // workdir vem do workspace root — nunca do profile do usuário.

    [Fact]
    public async Task Dado_ThreadSemWorkspace_Quando_Resolve_Entao_WorkdirDoWorkspaceRoot()
    {
        var thread = PtyThread("", AgentType.Claude);
        var (sut, _, _, discovery) = Create(thread);
        discovery.ResolveExecutablePath(AgentType.Claude).Returns("/usr/bin/claude");

        var (result, error) = await sut.ResolveAsync(thread.Id.Value);

        error.ShouldBeNull();
        result!.WorkingDirectory.ShouldBe(
            Path.Combine(Path.GetTempPath(), "resolver-ws"));
        result.WorkingDirectory.ShouldNotBe(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    [Fact]
    public void Dado_ThreadId_Quando_SessionIdFor_Entao_PrefixoT() =>
        ThreadPtyResolver.SessionIdFor("abc").ShouldBe("t-abc");
}
