using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.AiChat;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Application.Contracts.Workspace;
using Taskboard.Domain.Entities;
using Taskboard.Dtos;
using Taskboard.Repositories;
using Taskboard.Requests;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.AiChat;

/// <summary>
/// SPEC-20260921-ai-code-thread-config RF-003/RF-004/RF-006: resolução de
/// workspace a partir do repositório, seletor duplo tier/modelo e auditoria
/// da origem do modelo escolhido.
/// </summary>
public class AiChatThreadModelChoiceTests
{
    [Fact]
    public async Task Dado_ModeloExplicitoETier_Quando_CriarThread_Entao_ModeloExplicitoVence()
    {
        // Covers RF-004: modelo explícito vence o tier.
        var sut = CriarServico(eligible: [AgentType.OpenCode]);

        var dto = await sut.CreateThreadAsync(
            new CreateAiChatThreadRequest(
                "t", "opencode/claude-sonnet-5", "none", "read-only",
                AgentType: "OpenCode", ModelTier: "Ultra"),
            Actor.LocalUser());

        dto.Model.ShouldBe("opencode/claude-sonnet-5");
        dto.ModelTier.ShouldBe("Ultra");
    }

    [Fact]
    public async Task Dado_SemModeloComTierUltra_Quando_CriarThread_Entao_ResolveModeloDoTier()
    {
        // Covers RF-004: vazio + tier → resolução via config service
        // (override ?? curated); RF-006: origem "override" → "custom".
        var config = Substitute.For<IAgentModelConfigService>();
        config.GetConfigAsync(AgentType.OpenCode, Arg.Any<CancellationToken>())
            .Returns(new AgentModelConfigDto(
                AgentType.OpenCode, true, "override",
                Lite: "opencode/claude-haiku-4-5",
                Normal: "opencode/claude-sonnet-5",
                Ultra: "opencode/claude-opus-5",
                Defaults: new AgentModelTierSet(null, null, null),
                Catalog: []));

        var sut = CriarServico(eligible: [AgentType.OpenCode], modelConfig: config);

        var dto = await sut.CreateThreadAsync(
            new CreateAiChatThreadRequest(
                "t", "", "none", "read-only",
                AgentType: "OpenCode", ModelTier: "Ultra"),
            Actor.LocalUser());

        dto.Model.ShouldBe("opencode/claude-opus-5");
        dto.ModelTier.ShouldBe("Ultra");
        dto.ModelSource.ShouldBe("custom");
    }

    [Fact]
    public async Task Dado_TierSemOverride_Quando_CriarThread_Entao_ModelSourceCurated()
    {
        // Covers RF-004/RF-006: sem override salvo a fonte é a tabela curada.
        var config = Substitute.For<IAgentModelConfigService>();
        config.GetConfigAsync(AgentType.OpenCode, Arg.Any<CancellationToken>())
            .Returns(new AgentModelConfigDto(
                AgentType.OpenCode, true, "default",
                Lite: null, Normal: "opencode/claude-sonnet-5", Ultra: null,
                Defaults: new AgentModelTierSet(null, null, null),
                Catalog: []));

        var sut = CriarServico(eligible: [AgentType.OpenCode], modelConfig: config);

        var dto = await sut.CreateThreadAsync(
            new CreateAiChatThreadRequest(
                "t", "", "none", "read-only",
                AgentType: "OpenCode", ModelTier: "Normal"),
            Actor.LocalUser());

        dto.Model.ShouldBe("opencode/claude-sonnet-5");
        dto.ModelSource.ShouldBe("curated");
    }

    [Fact]
    public async Task Dado_SemModeloSemTier_Quando_CriarThread_Entao_CliDefaultSemAuditoria()
    {
        // Covers RF-004: ambos vazios → CLI default.
        var sut = CriarServico(eligible: [AgentType.OpenCode]);

        var dto = await sut.CreateThreadAsync(
            new CreateAiChatThreadRequest("t", "", "none", "read-only", AgentType: "OpenCode"),
            Actor.LocalUser());

        dto.Model.ShouldBe("default");
        dto.ModelTier.ShouldBeNull();
        dto.ModelSource.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_TierInvalido_Quando_CriarThread_Entao_InvalidValue()
    {
        var sut = CriarServico(eligible: [AgentType.OpenCode]);

        var ex = await Should.ThrowAsync<DomainException>(() => sut.CreateThreadAsync(
            new CreateAiChatThreadRequest(
                "t", "", "none", "read-only",
                AgentType: "OpenCode", ModelTier: "bogus"),
            Actor.LocalUser()));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidValue);
    }

    [Fact]
    public async Task Dado_ModeloReportadoPeloCli_Quando_CriarThread_Entao_SourceProbe()
    {
        // Covers RF-006: nome presente no probe do CLI → fonte "probe".
        var probe = Substitute.For<IAgentModelCatalogService>();
        probe.ListAvailableAsync(AgentType.OpenCode, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(["opencode/gpt-9-x"]);

        var sut = CriarServico(eligible: [AgentType.OpenCode], probe: probe);

        var dto = await sut.CreateThreadAsync(
            new CreateAiChatThreadRequest("t", "opencode/gpt-9-x", "none", "read-only", AgentType: "OpenCode"),
            Actor.LocalUser());

        dto.ModelSource.ShouldBe("probe");
    }

    [Fact]
    public async Task Dado_ModeloDaTabelaCurada_Quando_CriarThread_Entao_SourceCurated()
    {
        // Covers RF-006: nome da tabela curada (fora do probe) → "curated".
        var probe = Substitute.For<IAgentModelCatalogService>();
        probe.ListAvailableAsync(AgentType.Claude, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(["outro-modelo-qualquer"]);

        var sut = CriarServico(eligible: [AgentType.Claude], probe: probe);

        var dto = await sut.CreateThreadAsync(
            new CreateAiChatThreadRequest("t", "sonnet", "none", "read-only", AgentType: "Claude"),
            Actor.LocalUser());

        dto.ModelSource.ShouldBe("curated");
    }

    [Fact]
    public async Task Dado_ModeloCustom_Quando_CriarThread_Entao_SourceCustom()
    {
        // Covers RF-006: nome fora do probe e da tabela curada (ex.: entrada
        // registrada via POST /api/local/ai/catalog) → "custom".
        var probe = Substitute.For<IAgentModelCatalogService>();
        probe.ListAvailableAsync(AgentType.Claude, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());

        var sut = CriarServico(eligible: [AgentType.Claude], probe: probe);

        var dto = await sut.CreateThreadAsync(
            new CreateAiChatThreadRequest("t", "meu-modelo", "none", "read-only", AgentType: "Claude"),
            Actor.LocalUser());

        dto.ModelSource.ShouldBe("custom");
    }

    [Fact]
    public async Task Dado_AgentModeComRepoSemWorkspace_Quando_CriarThread_Entao_ResolveWorkspace()
    {
        // Covers RF-003: Mode=agent ∧ WorkspacePath vazio ∧ repo → resolve ~/repos/<repo>.
        var resolver = Substitute.For<IWorkspacePathResolver>();
        resolver.ResolveCardWorkdir("owner/meu-repo", out Arg.Any<bool>())
            .Returns(ci =>
            {
                ci[1] = true;
                return "/home/dev/repos/meu-repo";
            });

        var sut = CriarServico(eligible: [AgentType.OpenCode], workspace: resolver);

        var dto = await sut.CreateThreadAsync(
            new CreateAiChatThreadRequest(
                "t", "", "none", "workspace-write",
                Mode: "agent", AgentType: "OpenCode",
                RepositoryFullName: "owner/meu-repo"),
            Actor.LocalUser());

        dto.WorkspacePath.ShouldBe("/home/dev/repos/meu-repo");
        dto.RepositoryFullName.ShouldBe("owner/meu-repo");
    }

    [Fact]
    public async Task Dado_AgentModeComRepoInexistente_Quando_CriarThread_Entao_InvalidValue()
    {
        // Covers RF-003: falha de resolução → erro claro, sem fallback silencioso.
        var resolver = Substitute.For<IWorkspacePathResolver>();
        resolver.ResolveCardWorkdir("owner/fantasma", out Arg.Any<bool>())
            .Returns(ci =>
            {
                ci[1] = false;
                return "/home/dev/repos";
            });

        var sut = CriarServico(eligible: [AgentType.OpenCode], workspace: resolver);

        var ex = await Should.ThrowAsync<DomainException>(() => sut.CreateThreadAsync(
            new CreateAiChatThreadRequest(
                "t", "", "none", "workspace-write",
                Mode: "agent", AgentType: "OpenCode",
                RepositoryFullName: "owner/fantasma"),
            Actor.LocalUser()));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidValue);
    }

    [Fact]
    public async Task Dado_AgentModeComWorkspaceExplicito_Quando_CriarThread_Entao_ManualVence()
    {
        // Covers RF-003: WorkspacePath explícito sempre vence a resolução.
        var resolver = Substitute.For<IWorkspacePathResolver>();
        var sut = CriarServico(eligible: [AgentType.OpenCode], workspace: resolver);

        var dto = await sut.CreateThreadAsync(
            new CreateAiChatThreadRequest(
                "t", "", "none", "workspace-write",
                Mode: "agent", AgentType: "OpenCode",
                WorkspacePath: "/meu/workspace",
                RepositoryFullName: "owner/meu-repo"),
            Actor.LocalUser());

        dto.WorkspacePath.ShouldBe("/meu/workspace");
        resolver.DidNotReceiveWithAnyArgs().ResolveCardWorkdir(default, out _);
    }

    private static AiChatService CriarServico(
        AgentType[] eligible,
        IWorkspacePathResolver? workspace = null,
        IAgentModelConfigService? modelConfig = null,
        IAgentModelCatalogService? probe = null,
        IRepository<AiChatThread>? threadRepo = null)
    {
        var eligibility = Substitute.For<IAgentEligibilityService>();
        eligibility.GetEligibleTypesAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<AgentType>(eligible));

        var config = modelConfig;
        if (config is null)
        {
            config = Substitute.For<IAgentModelConfigService>();
            config.GetConfigAsync(Arg.Any<AgentType>(), Arg.Any<CancellationToken>())
                .Returns(ci => new AgentModelConfigDto(
                    ci.Arg<AgentType>(), true, "default",
                    null, null, null,
                    new AgentModelTierSet(null, null, null), []));
        }

        var modelProbe = probe;
        if (modelProbe is null)
        {
            modelProbe = Substitute.For<IAgentModelCatalogService>();
            modelProbe.ListAvailableAsync(Arg.Any<AgentType>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<string>());
        }

        var ws = workspace;
        if (ws is null)
        {
            ws = Substitute.For<IWorkspacePathResolver>();
            ws.ResolveCardWorkdir(Arg.Any<string>(), out Arg.Any<bool>())
                .Returns(ci =>
                {
                    ci[1] = false;
                    return "/repos";
                });
        }

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
            ws,
            config,
            modelProbe);
    }
}
