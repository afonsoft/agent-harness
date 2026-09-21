using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.AiChat;
using Taskboard.Application.Contracts.Agents;
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
    public async Task Dado_SemAgentesElegiveis_Quando_Listar_Entao_SomenteCustom()
    {
        var custom = new AiCatalogService();
        custom.TryAdd(new AiChatModelDto("custom-1", "test", "Custom", false, "OpenCode"));

        var sut = CriarServico(eligible: [], custom: custom);

        var models = await sut.ListAsync();

        models.ShouldBe([custom.List().Single()]);
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

    private static AiChatCatalogService CriarServico(
        AgentType[] eligible,
        IReadOnlyList<string>? probeModels = null,
        AiCatalogService? custom = null,
        IAgentModelCatalogService? probes = null)
    {
        var eligibility = Substitute.For<IAgentEligibilityService>();
        eligibility.GetEligibleTypesAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<AgentType>(eligible));

        probes ??= CriarProbes(probeModels ?? []);
        var modelConfig = Substitute.For<IAgentModelConfigService>();
        modelConfig.GetConfigAsync(Arg.Any<AgentType>(), Arg.Any<CancellationToken>())
            .Returns(new AgentModelConfigDto(
                AgentType.Codex, true, "curated", null, null, null,
                new AgentModelTierSet(null, null, null), []));

        return new AiChatCatalogService(custom ?? new AiCatalogService(), eligibility, probes, modelConfig);
    }

    private static IAgentModelCatalogService CriarProbes(IReadOnlyList<string> models)
    {
        var probes = Substitute.For<IAgentModelCatalogService>();
        probes.ListAvailableAsync(Arg.Any<AgentType>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(models);
        return probes;
    }
}
