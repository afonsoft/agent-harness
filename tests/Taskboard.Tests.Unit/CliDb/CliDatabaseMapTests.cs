using Shouldly;
using Taskboard.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.CliDb;

public class CliDatabaseMapTests
{
    [Fact]
    public void Dado_AgentCliKind_Quando_ConsultarMapa_Entao_TodoKindTemEntrada()
    {
        foreach (var kind in Enum.GetValues<AgentCliKind>())
        {
            CliDatabaseMap.SourcesFor(kind).ShouldNotBeNull($"{kind} deve ter entrada no mapa");
        }
    }

    [Fact]
    public void Dado_InventarioVerificado_Quando_ConsultarMapa_Entao_SourcesEsperadasExistem()
    {
        CliDatabaseMap.SourcesFor(AgentCliKind.Codex).ShouldNotBeEmpty();
        CliDatabaseMap.SourcesFor(AgentCliKind.OpenCode).ShouldNotBeEmpty();
        CliDatabaseMap.SourcesFor(AgentCliKind.Devin).ShouldNotBeEmpty();
        CliDatabaseMap.SourcesFor(AgentCliKind.Antigravity).ShouldNotBeEmpty();
        CliDatabaseMap.SourcesFor(AgentCliKind.Cline).ShouldNotBeEmpty();
        CliDatabaseMap.SourcesFor(AgentCliKind.Claude).ShouldNotBeEmpty();
    }

    [Fact]
    public void Dado_KindSemBanco_Quando_ConsultarMapa_Entao_ListaVazia()
    {
        CliDatabaseMap.SourcesFor(AgentCliKind.Kimi).ShouldBeEmpty();
        CliDatabaseMap.SourcesFor(AgentCliKind.Grok).ShouldBeEmpty();
        CliDatabaseMap.SourcesFor(AgentCliKind.Aider).ShouldBeEmpty();
        CliDatabaseMap.SourcesFor(AgentCliKind.Continue).ShouldBeEmpty();
        CliDatabaseMap.SourcesFor(AgentCliKind.Copilot).ShouldBeEmpty();
        CliDatabaseMap.SourcesFor(AgentCliKind.Qwen).ShouldBeEmpty();
        CliDatabaseMap.SourcesFor(AgentCliKind.Kiro).ShouldBeEmpty();
    }

    [Fact]
    public void Dado_QualquerSource_Quando_Inspecionar_Entao_PathRelativoSemTraversal()
    {
        foreach (var kind in Enum.GetValues<AgentCliKind>())
        {
            foreach (var source in CliDatabaseMap.SourcesFor(kind))
            {
                source.RelativePathPattern.ShouldNotStartWith("/", customMessage: $"{source.Name} deve ser relativo ao HOME");
                source.RelativePathPattern.ShouldNotStartWith("~", customMessage: $"{source.Name} não deve conter '~' — expansão é do locator");
                source.RelativePathPattern.ShouldNotContain("..", customMessage: $"{source.Name} não deve conter traversal");
                Path.IsPathFullyQualified(source.RelativePathPattern).ShouldBeFalse();
            }
        }
    }

    [Fact]
    public void Dado_QualquerSource_Quando_Inspecionar_Entao_WhitelistNaoVaziaESemSobreposicaoComDenied()
    {
        foreach (var kind in Enum.GetValues<AgentCliKind>())
        {
            foreach (var source in CliDatabaseMap.SourcesFor(kind))
            {
                source.WhitelistTables.ShouldNotBeEmpty(customMessage: $"{source.Name} deve declarar tabelas whitelist");
                source.WhitelistTables.Intersect(source.DeniedTables, StringComparer.OrdinalIgnoreCase)
                    .ShouldBeEmpty(customMessage: $"{source.Name}: whitelist ∩ denied deve ser vazio");
            }
        }
    }

    [Fact]
    public void Dado_OpenCodeECline_Quando_Inspecionar_Entao_TabelasDeCredencialNegadas()
    {
        var openCode = CliDatabaseMap.SourcesFor(AgentCliKind.OpenCode).SelectMany(s => s.DeniedTables);
        openCode.ShouldContain("credential");
        openCode.ShouldContain("account");

        var cline = CliDatabaseMap.SourcesFor(AgentCliKind.Cline).SelectMany(s => s.DeniedTables);
        cline.ShouldContain("connectors");
    }

    [Fact]
    public void Dado_SourceComGlob_Quando_Inspecionar_Entao_IsGlobTrue()
    {
        CliDatabaseMap.SourcesFor(AgentCliKind.Antigravity)
            .ShouldContain(s => s.IsGlob && s.RelativePathPattern.Contains('*'));
        CliDatabaseMap.SourcesFor(AgentCliKind.OpenCode)
            .ShouldAllBe(s => !s.IsGlob);
    }

    [Fact]
    public void Dado_SourceClaude_Quando_Inspecionar_Entao_Experimental()
    {
        CliDatabaseMap.SourcesFor(AgentCliKind.Claude).ShouldAllBe(s => s.Experimental);
    }
}
