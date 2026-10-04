using Shouldly;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20261004-builtin-model-probe: ModelListArgs do AgentCliSpec alimenta
/// ModelListProbe (formato Lines) — uma declaração só cobre o catálogo do
/// chat, o diálogo de Models, o snapshot e o endpoint builtin.
/// </summary>
public class AgentCliModelProbeTests
{
    [Fact]
    public void Dado_Grok_Quando_ModelListProbe_Entao_DerivaLinesDoSpec()
    {
        var probe = AgentCliModels.ModelListProbe(AgentType.Grok);

        probe.ShouldNotBeNull();
        probe.Arguments.ShouldBe(["models"]);
        probe.Format.ShouldBe(AgentModelListFormat.Lines);
    }

    [Fact]
    public void Dado_OpenCode_Quando_ModelListProbe_Entao_LinesModels()
    {
        var probe = AgentCliModels.ModelListProbe(AgentType.OpenCode);

        probe.ShouldNotBeNull();
        probe.Arguments.ShouldBe(["models"]);
        probe.Format.ShouldBe(AgentModelListFormat.Lines);
    }

    [Fact]
    public void Dado_Antigravity_Quando_ModelListProbe_Entao_FormatoTipadoPrevalece()
    {
        var probe = AgentCliModels.ModelListProbe(AgentType.Antigravity);

        probe.ShouldNotBeNull();
        probe.Format.ShouldBe(AgentModelListFormat.TabSeparated);
    }

    [Fact]
    public void Dado_Devin_Quando_ModelListProbe_Entao_FormatoTipadoPrevalece()
    {
        var probe = AgentCliModels.ModelListProbe(AgentType.Devin);

        probe.ShouldNotBeNull();
        probe.Format.ShouldBe(AgentModelListFormat.DevinModelsList);
    }

    [Fact]
    public void Dado_Claude_Quando_ModelListProbe_Entao_Null()
    {
        // Sem comando de listagem documentado — curated table only.
        AgentCliModels.ModelListProbe(AgentType.Claude).ShouldBeNull();
    }

    [Fact]
    public void Dado_AgentTypeSemCli_Quando_ModelListProbe_Entao_Null()
    {
        AgentCliModels.ModelListProbe(AgentType.OpenHands).ShouldBeNull();
    }
}
