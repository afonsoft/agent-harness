using Shouldly;
using Taskboard.Integrations.Skills;
using Xunit;

namespace Taskboard.Tests.Unit.Integrations.Skills;

/// <summary>
/// SPEC-20261010-mcp-skills-hub RF-005: parser tolerante de
/// <c>npx skills find</c> — tokens <c>owner/repo[@skill]</c> viram linhas
/// instaláveis, formatos desconhecidos degradam para linha de nome.
/// </summary>
public class SkillsSearchServiceTests
{
    [Fact]
    public void Dado_SaidaComRepos_Quando_Parse_Entao_ResultadosComRepository()
    {
        var results = SkillsSearchService.Parse("""
            Searching skills.sh for 'review'...
            afonsoft/skills@code-review — Reviews a PR for quality issues
            community/packs@qa-analyst
            Found 2 skills.
            """);

        results.Count.ShouldBe(2);
        results[0].Name.ShouldBe("code-review");
        results[0].Repository.ShouldBe("afonsoft/skills");
        results[0].Skill.ShouldBe("code-review");
        results[0].Description.ShouldNotBeNull().ShouldContain("Reviews a PR");
        results[1].Repository.ShouldBe("community/packs");
        results[1].Skill.ShouldBe("qa-analyst");
    }

    [Fact]
    public void Dado_SaidaComRepoSemSkill_Quando_Parse_Entao_RepositorySemArroba()
    {
        var results = SkillsSearchService.Parse("owner/repo - A skill collection");

        results.Single().Name.ShouldBe("repo");
        results[0].Repository.ShouldBe("owner/repo");
        results[0].Skill.ShouldBeNull();
    }

    [Fact]
    public void Dado_LinhaSemToken_Quando_Parse_Entao_DegradaParaNome()
    {
        var results = SkillsSearchService.Parse("some unrecognized line");

        results.Single().Name.ShouldBe("some unrecognized line");
        results[0].Repository.ShouldBeEmpty();
    }

    [Fact]
    public void Dado_SaidaGrande_Quando_Parse_Entao_Cap20()
    {
        var lines = string.Join('\n', Enumerable.Range(1, 40).Select(i => $"o{i}/r{i} - skill {i}"));

        var results = SkillsSearchService.Parse(lines);

        results.Count.ShouldBe(SkillsSearchService.MaxResults);
    }

    [Fact]
    public void Dado_SaidaVazia_Quando_Parse_Entao_Vazio()
    {
        SkillsSearchService.Parse("").ShouldBeEmpty();
        SkillsSearchService.Parse("\n\n").ShouldBeEmpty();
    }
}
