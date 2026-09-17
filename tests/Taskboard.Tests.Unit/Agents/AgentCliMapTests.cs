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
}
