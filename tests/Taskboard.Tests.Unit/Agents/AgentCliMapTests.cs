using Taskboard.Agents;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

public class AgentCliMapTests
{
    [Fact]
    public void Dado_TodosOsTipos_Quando_GetSpec_Entao_RetornaEspecificacaoCompleta()
    {
        foreach (var kind in Enum.GetValues<AgentCliKind>())
        {
            var spec = AgentCliMap.GetSpec(kind);

            spec.ShouldNotBeNull($"CLI {kind} deve ter spec mapeada");
            spec.DisplayName.ShouldNotBeNullOrWhiteSpace();
            spec.Binary.ShouldNotBeNullOrWhiteSpace();
            spec.LoginCommand.ShouldNotBeNullOrWhiteSpace();
            spec.InstallHint.ShouldNotBeNullOrWhiteSpace();
            spec.ConfigDirDisplay.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Dado_ListaAll_Quando_Invocada_Entao_ContemTodosOsTiposOrdenados()
    {
        var all = AgentCliMap.All;

        all.Count.ShouldBe(Enum.GetValues<AgentCliKind>().Length);
        all.Select(kv => kv.Key).ShouldBeSubsetOf(Enum.GetValues<AgentCliKind>());
        all.Select(kv => kv.Value.DisplayName).ShouldBe(
            all.Select(kv => kv.Value.DisplayName).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void Dado_TipoDesconhecido_Quando_GetSpec_Entao_RetornaNulo()
    {
        AgentCliMap.GetSpec((AgentCliKind)999).ShouldBeNull();
    }

    [Fact]
    public void Dado_CadaCliKind_Quando_AgentTypeFor_Entao_RetornaTipoCorrespondente()
    {
        AgentCliMap.AgentTypeFor(AgentCliKind.Devin).ShouldBe(AgentType.Devin);
        AgentCliMap.AgentTypeFor(AgentCliKind.Claude).ShouldBe(AgentType.Claude);
        AgentCliMap.AgentTypeFor(AgentCliKind.Codex).ShouldBe(AgentType.Codex);
        AgentCliMap.AgentTypeFor(AgentCliKind.OpenCode).ShouldBe(AgentType.OpenCode);
        AgentCliMap.AgentTypeFor(AgentCliKind.Antigravity).ShouldBe(AgentType.Antigravity);
    }

    [Fact]
    public void Dado_CadaAgentTypeComCli_Quando_CliKindFor_Entao_RetornaKindCorrespondente()
    {
        AgentCliMap.CliKindFor(AgentType.Devin).ShouldBe(AgentCliKind.Devin);
        AgentCliMap.CliKindFor(AgentType.Claude).ShouldBe(AgentCliKind.Claude);
        AgentCliMap.CliKindFor(AgentType.Codex).ShouldBe(AgentCliKind.Codex);
        AgentCliMap.CliKindFor(AgentType.OpenCode).ShouldBe(AgentCliKind.OpenCode);
        AgentCliMap.CliKindFor(AgentType.Antigravity).ShouldBe(AgentCliKind.Antigravity);
    }

    [Fact]
    public void Dado_OpenHands_Quando_CliKindFor_Entao_RetornaNulo()
    {
        AgentCliMap.CliKindFor(AgentType.OpenHands).ShouldBeNull();
    }

    [Fact]
    public void Dado_CadaCliKind_Quando_RoundTrip_Entao_PreservaKind()
    {
        foreach (var kind in Enum.GetValues<AgentCliKind>())
        {
            var type = AgentCliMap.AgentTypeFor(kind);

            type.ShouldNotBeNull($"CLI {kind} deve mapear para um AgentType");
            AgentCliMap.CliKindFor(type.Value).ShouldBe(kind);
        }
    }
}
