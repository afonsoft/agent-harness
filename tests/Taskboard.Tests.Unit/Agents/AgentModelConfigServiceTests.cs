using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Task = System.Threading.Tasks.Task;
using Taskboard.Application.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Domain.Entities;
using Taskboard.Repositories;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20260918-agent-model-config: resolução override ?? curado e
/// persistência do override por CLI.
/// </summary>
public class AgentModelConfigServiceTests
{
    [Fact]
    public async Task Dado_SemOverride_Quando_ResolveModel_Entao_Curado()
    {
        var service = CriarServico([]);

        var model = await service.ResolveModelAsync(AgentType.Claude, AgentModelTier.Ultra);

        model.ShouldBe("opus");
    }

    [Fact]
    public async Task Dado_Override_Quando_ResolveModel_Entao_OverrideVenceCurado()
    {
        var service = CriarServico([]);
        await service.SetConfigAsync(AgentType.Claude,
            new SaveAgentModelConfigRequest("custom-lite", null, "custom-ultra"));

        var lite = await service.ResolveModelAsync(AgentType.Claude, AgentModelTier.Lite);
        var normal = await service.ResolveModelAsync(AgentType.Claude, AgentModelTier.Normal);
        var ultra = await service.ResolveModelAsync(AgentType.Claude, AgentModelTier.Ultra);

        lite.ShouldBe("custom-lite");
        normal.ShouldBe("sonnet"); // slot ausente → curado por tier
        ultra.ShouldBe("custom-ultra");
    }

    [Fact]
    public async Task Dado_OverrideSalvo_Quando_Delete_Entao_VoltaAoCurado()
    {
        var service = CriarServico([]);
        await service.SetConfigAsync(AgentType.Devin,
            new SaveAgentModelConfigRequest("x", "y", "z"));

        await service.DeleteConfigAsync(AgentType.Devin);

        var model = await service.ResolveModelAsync(AgentType.Devin, AgentModelTier.Normal);
        model.ShouldBe("swe");
    }

    [Fact]
    public async Task Dado_CliGerenciada_Quando_Set_Entao_ArgumentException()
    {
        var service = CriarServico([]);

        await Should.ThrowAsync<ArgumentException>(() =>
            service.SetConfigAsync(AgentType.Cline, new SaveAgentModelConfigRequest("x", null, null)));
    }

    [Fact]
    public async Task Dado_NomeMuitoLongo_Quando_Set_Entao_ArgumentException()
    {
        var service = CriarServico([]);

        await Should.ThrowAsync<ArgumentException>(() =>
            service.SetConfigAsync(AgentType.Claude,
                new SaveAgentModelConfigRequest(new string('x', 200), null, null)));
    }

    [Fact]
    public async Task Dado_Override_Quando_GetConfig_Entao_SourceOverrideEDefaultsSeparados()
    {
        var service = CriarServico([]);
        await service.SetConfigAsync(AgentType.Claude,
            new SaveAgentModelConfigRequest("custom-lite", null, null));

        var config = await service.GetConfigAsync(AgentType.Claude);

        config.Source.ShouldBe("override");
        config.Lite.ShouldBe("custom-lite");
        config.Defaults.Lite.ShouldBe("haiku"); // curated intacto
        config.Catalog.ShouldContain("sonnet");
    }

    private static AgentModelConfigService CriarServico(List<ConfigurationOverride> store)
    {
        var repo = Substitute.For<IRepository<ConfigurationOverride>>();
        repo.ListAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<ConfigurationOverride>>(store.ToList()));
        repo.AddAsync(Arg.Any<ConfigurationOverride>(), Arg.Any<CancellationToken>())
            .Returns(call => { store.Add(call.Arg<ConfigurationOverride>()); return Task.CompletedTask; });
        repo.UpdateAsync(Arg.Any<ConfigurationOverride>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.CompletedTask);
        repo.DeleteAsync(Arg.Any<ConfigurationOverride>(), Arg.Any<CancellationToken>())
            .Returns(call => { store.Remove(call.Arg<ConfigurationOverride>()); return Task.CompletedTask; });
        repo.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        return new AgentModelConfigService(repo);
    }
}
