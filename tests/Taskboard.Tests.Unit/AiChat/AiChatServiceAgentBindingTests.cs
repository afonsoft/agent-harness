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
        var discovery = Substitute.For<IAgentDiscoveryService>();
        discovery.ResolveExecutablePath(AgentType.Claude).Returns("/usr/bin/claude");

        var sut = CriarServico(eligible: [], discovery: discovery);

        var dto = await sut.CreateThreadAsync(
            new CreateAiChatThreadRequest("t", "m", "none", "read-only",
                AgentType: "Claude", Transport: "pty", ContainerContext: "dev"),
            Actor.LocalUser());

        dto.ContainerContext.ShouldBe("dev");
    }

    private static AiChatService CriarServico(
        AgentType[] eligible,
        IRepository<AiChatThread>? threadRepo = null,
        IAgentCliDefinitionRepository? cliDefinitions = null,
        IAgentDiscoveryService? discovery = null)
    {
        var eligibility = Substitute.For<IAgentEligibilityService>();
        eligibility.GetEligibleTypesAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<AgentType>(eligible));

        return new AiChatService(
            threadRepo ?? Substitute.For<IRepository<AiChatThread>>(),
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
            cliDefinitions ?? Substitute.For<IAgentCliDefinitionRepository>(),
            discovery ?? Substitute.For<IAgentDiscoveryService>());
    }
}
