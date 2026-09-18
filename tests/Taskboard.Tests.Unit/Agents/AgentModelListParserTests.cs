using Shouldly;
using Taskboard.Application.Contracts.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// Fixtures mirror the real stdout of `opencode models`, `agy models` and
/// `devin models list` captured on the host (2026-09-18).
/// </summary>
public class AgentModelListParserTests
{
    [Fact]
    public void Dado_SaidaOpencodeModels_Quando_Parse_Entao_RetornaUmIdPorLinha()
    {
        const string output = """
            opencode/big-pickle
            opencode/claude-fable-5
            opencode/claude-haiku-4-5
            opencode/claude-sonnet-5

            """;

        var models = AgentModelListParser.Parse(AgentModelListFormat.Lines, output);

        models.ShouldBe(["opencode/big-pickle", "opencode/claude-fable-5", "opencode/claude-haiku-4-5", "opencode/claude-sonnet-5"]);
    }

    [Fact]
    public void Dado_LinhasComTextoLivre_Quando_ParseLines_Entao_IgnoraBanners()
    {
        const string output = """
            Fetching available models...
            opencode/gpt-5.1
            Available models below
            opencode/gemini-3-pro
            """;

        var models = AgentModelListParser.Parse(AgentModelListFormat.Lines, output);

        models.ShouldBe(["opencode/gpt-5.1", "opencode/gemini-3-pro"]);
    }

    [Fact]
    public void Dado_SaidaAgyModels_Quando_Parse_Entao_RetornaIdsAntesDoTab()
    {
        const string output = "Fetching available models...\n" +
            "gemini-3.8-flash-low\tGemini 3.8 Flash (Low)\n" +
            "gemini-3.1-pro-low\tGemini 3.1 Pro (Low)\n" +
            "gemini-3.1-pro-high\tGemini 3.1 Pro (High)\n";

        var models = AgentModelListParser.Parse(AgentModelListFormat.TabSeparated, output);

        models.ShouldBe(["gemini-3.8-flash-low", "gemini-3.1-pro-low", "gemini-3.1-pro-high"]);
    }

    [Fact]
    public void Dado_SaidaDevinModelsList_Quando_Parse_Entao_RetornaFamiliasVariantesEAliases()
    {
        const string output = """
            Available models (48 families)

            Claude Opus 5 (claude-opus-5)
              aliases: opus
              claude-opus-5-medium                          Claude Opus 5 Medium  [1M context]
              claude-opus-5-low                             Claude Opus 5 Low  [1M context]
            SWE (swe)
              aliases: swe, devin
              swe-1.5                                       SWE 1.5  [fast]
            """;

        var models = AgentModelListParser.Parse(AgentModelListFormat.DevinModelsList, output);

        models.ShouldBe([
            "claude-opus-5", "opus", "claude-opus-5-medium", "claude-opus-5-low",
            "swe", "devin", "swe-1.5",
        ]);
    }

    [Fact]
    public void Dado_IdsDuplicados_Quando_Parse_Entao_RemoveDuplicatasMantendoOrdem()
    {
        const string output = "sonnet\nSONNET\nopus\n";

        var models = AgentModelListParser.Parse(AgentModelListFormat.Lines, output);

        models.ShouldBe(["sonnet", "opus"]);
    }

    [Fact]
    public void Dado_SaidaVazia_Quando_Parse_Entao_ListaVazia()
    {
        AgentModelListParser.Parse(AgentModelListFormat.Lines, string.Empty).ShouldBeEmpty();
        AgentModelListParser.Parse(AgentModelListFormat.TabSeparated, "  \n").ShouldBeEmpty();
        AgentModelListParser.Parse(AgentModelListFormat.DevinModelsList, "Available models (0 families)\n").ShouldBeEmpty();
    }
}
