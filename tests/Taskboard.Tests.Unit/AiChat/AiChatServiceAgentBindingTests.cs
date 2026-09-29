using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.AiChat;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Domain.Entities;
using Taskboard.Repositories;
using Taskboard.Requests;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.AiChat;

/// <summary>
/// SPEC-20260921-ai-chat-cli-backend: toda thread exige agente elegível e
/// threads legadas sem agente migram para o primeiro elegível no próximo run.
/// </summary>
public class AiChatServiceAgentBindingTests
{
    [Fact]
    public async Task Dado_SemAgentType_Quando_CriarThread_Entao_InvalidValue()
    {
        var sut = CriarServico(eligible: [AgentType.Codex]);

        var ex = await Should.ThrowAsync<DomainException>(() => sut.CreateThreadAsync(
            new CreateAiChatThreadRequest("t", "m", "none", "read-only"), Actor.LocalUser()));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidValue);
    }

    [Fact]
    public async Task Dado_AgenteNaoElegivel_Quando_CriarThread_Entao_AgentNotEligible()
    {
        var sut = CriarServico(eligible: [AgentType.Codex]);

        var ex = await Should.ThrowAsync<DomainException>(() => sut.CreateThreadAsync(
            new CreateAiChatThreadRequest("t", "m", "none", "read-only", AgentType: "Claude"),
            Actor.LocalUser()));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.AgentNotEligible);
    }

    [Fact]
    public async Task Dado_AgenteElegivel_Quando_CriarAssistant_Entao_AgentTypePersistido()
    {
        var sut = CriarServico(eligible: [AgentType.OpenCode]);

        var dto = await sut.CreateThreadAsync(
            new CreateAiChatThreadRequest("t", "m", "none", "read-only", AgentType: "OpenCode"),
            Actor.LocalUser());

        dto.Mode.ShouldBe("assistant");
        dto.AgentType.ShouldBe("OpenCode");
    }

    [Fact]
    public async Task Dado_ThreadLegadaSemAgente_Quando_StartRun_Entao_MigraParaPrimeiroElegivel()
    {
        var threadRepo = Substitute.For<IRepository<AiChatThread>>();
        var thread = AiChatThread.Create(AiChatThreadId.NewGuid(), "legacy", ModelRef.From("gpt-4o"), "medium", Sandbox.ReadOnly);
        threadRepo.GetAsync(thread.Id, Arg.Any<CancellationToken>()).Returns(thread);

        var sut = CriarServico(eligible: [AgentType.Codex, AgentType.OpenCode], threadRepo: threadRepo);

        await sut.StartRunAsync(thread.Id, Actor.LocalUser());

        // primeiro elegível por ordem alfabética do enum: Codex
        thread.AgentType.ShouldBe(AgentType.Codex);
        // modelo legado de provider (gpt-4o) não é da CLI → reseta para default
        thread.Model.Value.ShouldBe("default");
        await threadRepo.Received().UpdateAsync(thread, Arg.Any<CancellationToken>());
    }

    // SPEC-20260928-ai-code-generic-cli RF-002/RF-003: custom defs e transport pty.

    [Fact]
    public async Task Dado_CustomCliHabilitada_Quando_CriarThread_Entao_AgentCliIdETransportPty()
    {
        var defs = Substitute.For<IAgentCliDefinitionRepository>();
        var def = new Taskboard.Dtos.AgentCliDefinitionDto(
            "custom-minha", "Minha CLI", "minha-cli", "", "pty", null, "--version", true, true);
        defs.GetAsync("custom-minha", Arg.Any<CancellationToken>()).Returns(def);

        var sut = CriarServico(eligible: [], cliDefinitions: defs);

        var dto = await sut.CreateThreadAsync(
            new CreateAiChatThreadRequest("t", "m", "none", "read-only", AgentCliId: "custom-minha"),
            Actor.LocalUser());

        dto.AgentCliId.ShouldBe("custom-minha");
        dto.Transport.ShouldBe("pty");
        dto.Kind.ShouldBe("terminal");
        dto.AgentType.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_CustomCliDesabilitada_Quando_CriarThread_Entao_InvalidValue()
    {
        var defs = Substitute.For<IAgentCliDefinitionRepository>();
        var def = new Taskboard.Dtos.AgentCliDefinitionDto(
            "custom-off", "Off", "off-cli", "", "pty", null, "--version", false, true);
        defs.GetAsync("custom-off", Arg.Any<CancellationToken>()).Returns(def);

        var sut = CriarServico(eligible: [], cliDefinitions: defs);

        var ex = await Should.ThrowAsync<DomainException>(() => sut.CreateThreadAsync(
            new CreateAiChatThreadRequest("t", "m", "none", "read-only", AgentCliId: "custom-off"),
            Actor.LocalUser()));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidValue);
    }

    [Fact]
    public async Task Dado_CustomCliInexistente_Quando_CriarThread_Entao_InvalidValue()
    {
        var defs = Substitute.For<IAgentCliDefinitionRepository>();
        defs.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Taskboard.Dtos.AgentCliDefinitionDto?)null);

        var sut = CriarServico(eligible: [], cliDefinitions: defs);

        var ex = await Should.ThrowAsync<DomainException>(() => sut.CreateThreadAsync(
            new CreateAiChatThreadRequest("t", "m", "none", "read-only", AgentCliId: "custom-nope"),
            Actor.LocalUser()));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidValue);
    }

    [Fact]
    public async Task Dado_BuiltinInstaladoSemElegivel_Quando_CriarThreadPty_Entao_TerminalSemChecarAuth()
    {
        // PTY threads skip the authenticated-eligibility gate — only the
        // binary must resolve (auth happens inside the terminal).
        var discovery = Substitute.For<IAgentDiscoveryService>();
        discovery.ResolveExecutablePath(AgentType.Aider).Returns("/usr/bin/aider");

        var sut = CriarServico(eligible: [], discovery: discovery);

        var dto = await sut.CreateThreadAsync(
            new CreateAiChatThreadRequest("t", "m", "none", "read-only",
                AgentType: "Aider", Transport: "pty"),
            Actor.LocalUser());

        dto.Transport.ShouldBe("pty");
        dto.Kind.ShouldBe("terminal");
        dto.AgentType.ShouldBe("Aider");
    }

    [Fact]
    public async Task Dado_BuiltinNaoInstalado_Quando_CriarThreadPty_Entao_InvalidValue()
    {
        var discovery = Substitute.For<IAgentDiscoveryService>();
        discovery.ResolveExecutablePath(Arg.Any<AgentType>()).Returns((string?)null);

        var sut = CriarServico(eligible: [], discovery: discovery);

        var ex = await Should.ThrowAsync<DomainException>(() => sut.CreateThreadAsync(
            new CreateAiChatThreadRequest("t", "m", "none", "read-only",
                AgentType: "Aider", Transport: "pty"),
            Actor.LocalUser()));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidValue);
    }

    [Fact]
    public async Task Dado_TransporteInvalido_Quando_CriarThread_Entao_InvalidValue()
    {
        var discovery = Substitute.For<IAgentDiscoveryService>();
        discovery.ResolveExecutablePath(AgentType.Claude).Returns("/usr/bin/claude");

        var sut = CriarServico(eligible: [AgentType.Claude], discovery: discovery);

        var ex = await Should.ThrowAsync<DomainException>(() => sut.CreateThreadAsync(
            new CreateAiChatThreadRequest("t", "m", "none", "read-only",
                AgentType: "Claude", Transport: "websocket"),
            Actor.LocalUser()));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidValue);
    }

    [Fact]
    public async Task Dado_ContextoDocker_Quando_CriarThreadPty_Entao_ContainerContextPersistido()
    {
        // SPEC-20260929-docker-cli-context RF-002: com container, a CLI é
        // validada contra a descoberta do contêiner — não o PATH do host.
        var discovery = Substitute.For<IAgentDiscoveryService>();
        discovery.ResolveExecutablePath(Arg.Any<AgentType>()).Returns((string?)null);
        var containers = Substitute.For<IContainerCliDiscovery>();
        containers.ListContainersAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Taskboard.Dtos.DockerContainerDto>
            {
                new("dev", "ubuntu:24.04", ["claude"]),
            });

        var sut = CriarServico(eligible: [], discovery: discovery, containerDiscovery: containers);

        var dto = await sut.CreateThreadAsync(
            new CreateAiChatThreadRequest("t", "m", "none", "read-only",
                AgentType: "Claude", Transport: "pty", ContainerContext: "dev"),
            Actor.LocalUser());

        dto.ContainerContext.ShouldBe("dev");
    }

    [Fact]
    public async Task Dado_CliSoNoContainer_Quando_CriarThreadPty_Entao_AceitaSemInstalacaoNoHost()
    {
        var discovery = Substitute.For<IAgentDiscoveryService>();
        discovery.ResolveExecutablePath(Arg.Any<AgentType>()).Returns((string?)null);
        var containers = Substitute.For<IContainerCliDiscovery>();
        containers.ListContainersAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Taskboard.Dtos.DockerContainerDto>
            {
                new("dev", "img", ["aider"]),
            });

        var sut = CriarServico(eligible: [], discovery: discovery, containerDiscovery: containers);

        var dto = await sut.CreateThreadAsync(
            new CreateAiChatThreadRequest("t", "m", "none", "read-only",
                AgentType: "Aider", Transport: "pty", ContainerContext: "dev"),
            Actor.LocalUser());

        dto.Transport.ShouldBe("pty");
        dto.ContainerContext.ShouldBe("dev");
    }

    [Fact]
    public async Task Dado_CliAusenteNoContainer_Quando_CriarThreadPty_Entao_InvalidValue()
    {
        var containers = Substitute.For<IContainerCliDiscovery>();
        containers.ListContainersAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Taskboard.Dtos.DockerContainerDto>
            {
                new("dev", "img", ["claude"]),
            });

        var sut = CriarServico(eligible: [], containerDiscovery: containers);

        var ex = await Should.ThrowAsync<DomainException>(() => sut.CreateThreadAsync(
            new CreateAiChatThreadRequest("t", "m", "none", "read-only",
                AgentType: "Aider", Transport: "pty", ContainerContext: "dev"),
            Actor.LocalUser()));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidValue);
    }

    [Fact]
    public async Task Dado_ContainerDesconhecido_Quando_CriarThreadPty_Entao_InvalidValue()
    {
        var containers = Substitute.For<IContainerCliDiscovery>();
        containers.ListContainersAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Taskboard.Dtos.DockerContainerDto>
            {
                new("dev", "img", ["claude"]),
            });

        var sut = CriarServico(eligible: [], containerDiscovery: containers);

        var ex = await Should.ThrowAsync<DomainException>(() => sut.CreateThreadAsync(
            new CreateAiChatThreadRequest("t", "m", "none", "read-only",
                AgentType: "Claude", Transport: "pty", ContainerContext: "ghost"),
            Actor.LocalUser()));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidValue);
    }

    [Fact]
    public async Task Dado_ChatEmContainerComCliSemAcp_Quando_CriarThread_Entao_InvalidValue()
    {
        // Aider é PTY-only: mesmo dentro do contêiner não serve Chat (ACP).
        var containers = Substitute.For<IContainerCliDiscovery>();
        containers.ListContainersAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Taskboard.Dtos.DockerContainerDto>
            {
                new("dev", "img", ["aider"]),
            });

        var sut = CriarServico(eligible: [], containerDiscovery: containers);

        var ex = await Should.ThrowAsync<DomainException>(() => sut.CreateThreadAsync(
            new CreateAiChatThreadRequest("t", "m", "none", "read-only",
                AgentType: "Aider", Transport: "acp", ContainerContext: "dev"),
            Actor.LocalUser()));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidValue);
        ex.Message.ShouldContain("ACP");
    }

    [Fact]
    public async Task Dado_ChatEmContainerComCliAcp_Quando_CriarThread_Entao_AceitaSemElegibilidadeHost()
    {
        // Claude dentro do contêiner: Chat (ACP) vale mesmo sem CLI elegível no host.
        var containers = Substitute.For<IContainerCliDiscovery>();
        containers.ListContainersAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Taskboard.Dtos.DockerContainerDto>
            {
                new("dev", "img", ["claude"]),
            });

        var sut = CriarServico(eligible: [], containerDiscovery: containers);

        var dto = await sut.CreateThreadAsync(
            new CreateAiChatThreadRequest("t", "m", "none", "read-only",
                AgentType: "Claude", Transport: "acp", ContainerContext: "dev"),
            Actor.LocalUser());

        dto.ContainerContext.ShouldBe("dev");
        dto.AgentType.ShouldBe("Claude");
    }

    // SPEC-20260929-ai-chat-capabilities RF-001/RF-002/RF-003.

    [Fact]
    public async Task Dado_CliSemAcpNoHost_Quando_CriarChat_Entao_InvalidValue()
    {
        // Aider elegível mas PTY-only: o gate de capability vence a elegibilidade.
        var discovery = Substitute.For<IAgentDiscoveryService>();
        discovery.ResolveExecutablePath(AgentType.Aider).Returns("/usr/bin/aider");

        var sut = CriarServico(eligible: [AgentType.Aider], discovery: discovery);

        var ex = await Should.ThrowAsync<DomainException>(() => sut.CreateThreadAsync(
            new CreateAiChatThreadRequest("t", "m", "none", "read-only",
                AgentType: "Aider", Transport: "acp"),
            Actor.LocalUser()));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidValue);
        ex.Message.ShouldContain("ACP");
    }

    [Fact]
    public async Task Dado_CustomCliComTransporteAcp_Quando_CriarThread_Entao_InvalidValue()
    {
        // Custom defs spawn via argv (PTY) — chat estruturado exige adapter ACP
        // por AgentType; a criação recusa em vez de gerar thread morta.
        var defs = Substitute.For<IAgentCliDefinitionRepository>();
        defs.GetAsync("custom-acp", Arg.Any<CancellationToken>()).Returns(
            new Taskboard.Dtos.AgentCliDefinitionDto(
                "custom-acp", "ACP Custom", "mycli", "", "acp", null, "--version", true, true));

        var sut = CriarServico(eligible: [], cliDefinitions: defs);

        var ex = await Should.ThrowAsync<DomainException>(() => sut.CreateThreadAsync(
            new CreateAiChatThreadRequest("t", "m", "none", "read-only",
                AgentCliId: "custom-acp"),
            Actor.LocalUser()));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidValue);
        ex.Message.ShouldContain("chat view");
    }

    [Fact]
    public async Task Dado_ThreadComCliEContexto_Quando_Fork_Entao_PreservaBinding()
    {
        var dbPath = Path.Join(Path.GetTempPath(), $"tb-fork-{Guid.NewGuid()}.sqlite");
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Taskboard.EntityFrameworkCore.Data.TaskboardDbContext>()
            .UseSqlite($"Data Source={dbPath};Pooling=false")
            .Options;
        await using var ctx = new Taskboard.EntityFrameworkCore.Data.TaskboardDbContext(options);
        await ctx.Database.EnsureCreatedAsync();

        var source = AiChatThread.CreateAgentThread(
            AiChatThreadId.NewGuid(), "orig", ModelRef.From("default"), "medium",
            Sandbox.ReadOnly, AgentType.Claude, workspacePath: "/work");
        source.ConfigureCli("pty", "dev", agentCliId: null);
        var first = AiChatEvent.CreateTyped(
            AiChatEventId.NewGuid(), source.Id, AiChatEventRole.User, "hello", AiChatEventKind.Message);
        ctx.Set<AiChatThread>().Add(source);
        ctx.Set<AiChatEvent>().Add(first);
        await ctx.SaveChangesAsync();

        var threadRepo = new Taskboard.EntityFrameworkCore.Repositories.EfCoreRepository<AiChatThread>(ctx);
        var eventRepo = new Taskboard.EntityFrameworkCore.Repositories.EfCoreRepository<AiChatEvent>(ctx);
        var sut = CriarServico(eligible: [], threadRepo: threadRepo, eventRepo: eventRepo);

        var dto = await sut.ForkThreadAsync(source.Id, first.Id.Value, Actor.LocalUser());

        dto.Transport.ShouldBe("pty");
        dto.ContainerContext.ShouldBe("dev");
        dto.AgentType.ShouldBe("Claude");

        ctx.Dispose();
        File.Delete(dbPath);
    }

    private static AiChatService CriarServico(
        AgentType[] eligible,
        IRepository<AiChatThread>? threadRepo = null,
        IAgentCliDefinitionRepository? cliDefinitions = null,
        IAgentDiscoveryService? discovery = null,
        IContainerCliDiscovery? containerDiscovery = null,
        IRepository<AiChatEvent>? eventRepo = null)
    {
        var eligibility = Substitute.For<IAgentEligibilityService>();
        eligibility.GetEligibleTypesAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<AgentType>(eligible));

        return new AiChatService(
            threadRepo ?? Substitute.For<IRepository<AiChatThread>>(),
            Substitute.For<IRepository<AiChatRun>>(),
            eventRepo ?? Substitute.For<IRepository<AiChatEvent>>(),
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
            cliDefinitions ?? Substitute.For<IAgentCliDefinitionRepository>(),
            discovery ?? Substitute.For<IAgentDiscoveryService>(),
            containerDiscovery ?? Substitute.For<IContainerCliDiscovery>());
    }
}
