using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.AiChat;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Dtos;
using Xunit;

namespace Taskboard.Tests.Unit.AiChat;

public class AiChatCatalogServiceTests
{
    [Fact]
    public async Task Dado_AgenteElegivelComProbe_Quando_Listar_Entao_ModelosDaCliSemHardcoded()
    {
        var sut = CriarServico(
            eligible: [AgentType.OpenCode],
            probeModels: ["opencode/claude-sonnet-5", "opencode/extra-1"]);

        var models = await sut.ListAsync();

        models.ShouldAllBe(m => m.AgentType == "OpenCode");
        models.ShouldContain(m => m.Name == "opencode/extra-1");
        models.ShouldContain(m => m.Name == "opencode/claude-haiku-4-5"); // curated
        models.ShouldNotContain(m => m.Name == "gpt-4o"); // hardcoded legacy removido
    }

    [Fact]
    public async Task Dado_ProbeFalha_Quando_Listar_Entao_CuradoContinuaDisponivel()
    {
        var probes = Substitute.For<IAgentModelCatalogService>();
        probes.ListAvailableAsync(Arg.Any<AgentType>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<string>>(_ => throw new InvalidOperationException("cli missing"));

        var sut = CriarServico(eligible: [AgentType.Claude], probes: probes);

        var models = await sut.ListAsync();

        models.ShouldNotBeEmpty();
        models.ShouldAllBe(m => m.AgentType == "Claude");
    }

    [Fact]
    public async Task Dado_SemAgentesElegiveis_Quando_Listar_Entao_CatalogoVazio()
    {
        // Custom entries bound to a now-ineligible agent must not be offered —
        // with zero eligible agents the catalog is empty.
        var custom = new AiCatalogService();
        custom.TryAdd(new AiChatModelDto("custom-1", "test", "Custom", false, "OpenCode"));

        var sut = CriarServico(eligible: [], custom: custom);

        var models = await sut.ListAsync();

        models.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_CustomDeAgenteElegivel_Quando_Listar_Entao_Inclui()
    {
        var custom = new AiCatalogService();
        custom.TryAdd(new AiChatModelDto("custom-1", "test", "Custom", false, "OpenCode"));

        var sut = CriarServico(eligible: [AgentType.OpenCode], custom: custom);

        var models = await sut.ListAsync();

        models.ShouldContain(m => m.Id == "custom-1");
    }

    [Fact]
    public async Task Dado_AgentTypeInvalido_Quando_Adicionar_Entao_InvalidAgent()
    {
        var sut = CriarServico(eligible: [AgentType.Codex]);

        var error = await sut.AddAsync(new AiChatModelDto("x", "p", "X", false, "not-an-agent"));

        error.ShouldBe("INVALID_AGENT");
    }

    [Fact]
    public async Task Dado_AgenteNaoElegivel_Quando_Adicionar_Entao_AgentNotEligible()
    {
        var sut = CriarServico(eligible: [AgentType.Codex]);

        var error = await sut.AddAsync(new AiChatModelDto("x", "p", "X", false, "Claude"));

        error.ShouldBe("agent-not-eligible");
    }

    [Fact]
    public async Task Dado_AgenteElegivel_Quando_Adicionar_Entao_SucessoEDuplicataConflita()
    {
        var sut = CriarServico(eligible: [AgentType.Codex]);
        var model = new AiChatModelDto("x", "p", "X", false, "Codex");

        (await sut.AddAsync(model)).ShouldBeNull();
        (await sut.AddAsync(model)).ShouldBe("MODEL_EXISTS");
    }

    [Fact]
    public async Task Dado_SessaoAcpComModelos_Quando_ListarPorThread_Entao_RetornaReportadosPeloAgente()
    {
        // Covers RF-005(a): configOptions da sessão ativa têm prioridade máxima.
        var reported = new List<AiChatModelDto>
        {
            new("OpenCode:opencode/acp-1", "OpenCode", "opencode/acp-1", false, "OpenCode", "acp"),
            new("OpenCode:opencode/acp-2", "OpenCode", "opencode/acp-2", false, "OpenCode", "acp"),
        };
        var session = Substitute.For<IAgentSessionModelCatalog>();
        session.GetModels("t1").Returns(reported);

        var sut = CriarServico(eligible: [AgentType.OpenCode], session: session);

        var models = await sut.ListAsync("t1");

        models.Count.ShouldBe(2);
        models.ShouldAllBe(m => m.Source == "acp");
        models.ShouldContain(m => m.Name == "opencode/acp-1");
    }

    [Fact]
    public async Task Dado_ThreadSemSessao_Quando_ListarPorThread_Entao_FallbackCatalogoGlobal()
    {
        // Covers RF-005: sem sessão/modelos ACP o catálogo global segue valendo.
        var session = Substitute.For<IAgentSessionModelCatalog>();
        session.GetModels("sem-sessao").Returns([]);

        var sut = CriarServico(eligible: [AgentType.OpenCode], session: session);

        var models = await sut.ListAsync("sem-sessao");

        models.ShouldNotBeEmpty();
        models.ShouldAllBe(m => m.Source != "acp");
    }

    [Fact]
    public async Task Dado_ProbeCuradoOverrideECustom_Quando_Listar_Entao_TagsDeOrigem()
    {
        // Covers RF-005(b-d): prioridade probe > curated > custom na tag.
        var custom = new AiCatalogService();
        custom.TryAdd(new AiChatModelDto("OpenCode:m-custom", "x", "m-custom", false, "OpenCode"));

        var modelConfig = Substitute.For<IAgentModelConfigService>();
        modelConfig.GetConfigAsync(Arg.Any<AgentType>(), Arg.Any<CancellationToken>())
            .Returns(new AgentModelConfigDto(
                AgentType.OpenCode, true, "override",
                Lite: "m-override", Normal: null, Ultra: null,
                new AgentModelTierSet(null, null, null), []));

        var sut = CriarServico(
            eligible: [AgentType.OpenCode],
            probeModels: ["m-probe"],
            custom: custom,
            modelConfig: modelConfig);

        var models = await sut.ListAsync();

        models.Single(m => m.Name == "m-probe").Source.ShouldBe("probe");
        models.Single(m => m.Name == "opencode/claude-sonnet-5").Source.ShouldBe("curated");
        models.Single(m => m.Name == "m-override").Source.ShouldBe("custom");
        models.Single(m => m.Name == "m-custom").Source.ShouldBe("custom");
    }

    [Fact]
    public async Task Dado_NomeNoProbeENaCurada_Quando_Listar_Entao_TagProbe()
    {
        // Covers RF-005: o mesmo nome em várias fontes fica com a de maior prioridade.
        var sut = CriarServico(
            eligible: [AgentType.OpenCode],
            probeModels: ["opencode/claude-sonnet-5"]);

        var models = await sut.ListAsync();

        var entries = models.Where(m => m.Name == "opencode/claude-sonnet-5").ToList();
        entries.Count.ShouldBe(1);
        entries[0].Source.ShouldBe("probe");
    }

    private static AiChatCatalogService CriarServico(
        AgentType[] eligible,
        IReadOnlyList<string>? probeModels = null,
        AiCatalogService? custom = null,
        IAgentModelCatalogService? probes = null,
        IAgentModelConfigService? modelConfig = null,
        IAgentSessionModelCatalog? session = null)
    {
        var eligibility = Substitute.For<IAgentEligibilityService>();
        eligibility.GetEligibleTypesAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<AgentType>(eligible));

        probes ??= CriarProbes(probeModels ?? []);
        if (modelConfig is null)
        {
            modelConfig = Substitute.For<IAgentModelConfigService>();
            modelConfig.GetConfigAsync(Arg.Any<AgentType>(), Arg.Any<CancellationToken>())
                .Returns(new AgentModelConfigDto(
                    AgentType.Codex, true, "curated", null, null, null,
                    new AgentModelTierSet(null, null, null), []));
        }

        session ??= Substitute.For<IAgentSessionModelCatalog>();

        return new AiChatCatalogService(
            custom ?? new AiCatalogService(),
            eligibility,
            probes,
            modelConfig,
            session,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<AiChatCatalogService>>());
    }

    private static IAgentModelCatalogService CriarProbes(IReadOnlyList<string> models)
    {
        var probes = Substitute.For<IAgentModelCatalogService>();
        probes.ListAvailableAsync(Arg.Any<AgentType>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(models);
        return probes;
    }
}
