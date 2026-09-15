using Shouldly;
using Taskboard.Blazor.Services;
using Xunit;

namespace Taskboard.Tests.Unit.Blazor;

/// <summary>
/// SPEC-20260915-wasm-post-migration-hardening RF-002: pure-logic coverage
/// for the logic extracted from RepositoryCombobox.
/// </summary>
public class RepositoryFilterTests
{
    private static readonly string[] Repositories =
    [
        "afonsoft/taskboard-ai",
        "afonsoft/skills",
        "afonsoft/LangGraph-UI",
        "octokit/octokit.net",
    ];

    [Fact]
    public void Dado_FiltroVazio_Quando_Filtrar_Entao_RetornaTodos()
    {
        var result = RepositoryFilter.Filter(Repositories, null);

        result.ShouldBe(Repositories);
    }

    [Fact]
    public void Dado_FiltroPorOwner_Quando_Filtrar_Entao_RetornaSomenteMatches()
    {
        var result = RepositoryFilter.Filter(Repositories, "afonsoft");

        result.Count.ShouldBe(3);
        result.ShouldAllBe(r => r.StartsWith("afonsoft/"));
    }

    [Fact]
    public void Dado_FiltroCaseDiferente_Quando_Filtrar_Entao_MatchCaseInsensitive()
    {
        var result = RepositoryFilter.Filter(Repositories, "LANGGRAPH");

        result.ShouldBe(["afonsoft/LangGraph-UI"]);
    }

    [Fact]
    public void Dado_FiltroSemMatch_Quando_Filtrar_Entao_RetornaVazio()
    {
        var result = RepositoryFilter.Filter(Repositories, "inexistente");

        result.ShouldBeEmpty();
    }

    [Fact]
    public void Dado_ListaMaiorQueCap_Quando_Filtrar_Entao_TruncaEmMaxResults()
    {
        var many = Enumerable.Range(0, 60).Select(i => $"org/repo-{i}").ToList();

        var result = RepositoryFilter.Filter(many, null);

        result.Count.ShouldBe(RepositoryFilter.MaxResults);
    }

    [Theory]
    [InlineData("afonsoft/taskboard-ai", true)]
    [InlineData("org.name/repo_1", true)]
    [InlineData("taskboard-ai", false)]
    [InlineData("a/b/c", false)]
    [InlineData("", false)]
    [InlineData(" owner /repo", false)]
    public void Dado_Texto_Quando_ValidarRepositoryName_Entao_SegueOwnerRepo(string value, bool expected)
    {
        RepositoryFilter.IsValidRepositoryName(value).ShouldBe(expected);
    }

    [Fact]
    public void Dado_IndiceNoUltimo_Quando_ArrowDown_Entao_VoltaParaPrimeiro()
    {
        RepositoryFilter.MoveActiveIndex(3, 4, 1).ShouldBe(0);
        RepositoryFilter.MoveActiveIndex(-1, 4, 1).ShouldBe(0);
    }

    [Fact]
    public void Dado_IndiceNoPrimeiroOuNenhum_Quando_ArrowUp_Entao_VaiParaUltimo()
    {
        RepositoryFilter.MoveActiveIndex(0, 4, -1).ShouldBe(3);
        RepositoryFilter.MoveActiveIndex(-1, 4, -1).ShouldBe(3);
        RepositoryFilter.MoveActiveIndex(2, 0, -1).ShouldBe(-1);
    }

    [Fact]
    public void Dado_IndiceForaDaFaixa_Quando_Clamp_Entao_RetornaMenosUm()
    {
        RepositoryFilter.ClampActiveIndex(-1, 4).ShouldBe(-1);
        RepositoryFilter.ClampActiveIndex(4, 4).ShouldBe(-1);
        RepositoryFilter.ClampActiveIndex(0, 0).ShouldBe(-1);
        RepositoryFilter.ClampActiveIndex(2, 4).ShouldBe(2);
    }

    [Fact]
    public void Dado_ValorComEspacos_Quando_NormalizarCommit_Entao_Trima()
    {
        RepositoryFilter.NormalizeCommit("  owner/repo  ").ShouldBe("owner/repo");
        RepositoryFilter.NormalizeCommit(null).ShouldBe(string.Empty);
    }
}
