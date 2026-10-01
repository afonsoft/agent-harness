using Shouldly;
using Taskboard.Rendering;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261001-ai-chat-openwebui RF-002: o collapse das tools renderiza o
/// resultado de forma legível — campos de texto verbatim, listas como linhas,
/// resultados de busca numerados, erro primeiro, ANSI removido.
/// </summary>
public sealed class ToolResultFormatterTests
{
    [Fact]
    public void Dado_TextoNaoJson_Quando_Formatar_Entao_RetornaComoEsta()
    {
        ToolResultFormatter.FormatForTerminal("saida simples\nlinha 2")
            .ShouldBe("saida simples\nlinha 2");
    }

    [Fact]
    public void Dado_EscapasAnsi_Quando_Formatar_Entao_Removidos()
    {
        ToolResultFormatter.FormatForTerminal("[31mvermelho[0m ok")
            .ShouldBe("vermelho ok");
    }

    [Fact]
    public void Dado_JsonComOutput_Quando_Formatar_Entao_TextoVerbatimMaisMetadados()
    {
        var result = ToolResultFormatter.FormatForTerminal(
            """{"exitCode":0,"output":"linha1\nlinha2"}""");

        result.ShouldContain("linha1\nlinha2");
        result.ShouldContain("exitCode: 0");
    }

    [Fact]
    public void Dado_JsonComErro_Quando_Formatar_Entao_ErroPrimeiro()
    {
        var result = ToolResultFormatter.FormatForTerminal(
            """{"error":"falhou","output":"x"}""");

        result.ShouldStartWith("error: falhou");
        result.ShouldContain("x");
    }

    [Fact]
    public void Dado_JsonComResultadosDeBusca_Quando_Formatar_Entao_ListaNumerada()
    {
        var result = ToolResultFormatter.FormatForTerminal(
            """{"results":[{"title":"T1","url":"https://a.test","snippet":"s1"},{"title":"T2","url":"https://b.test","snippet":"s2"}]}""");

        result.ShouldContain("1. T1\n   https://a.test\n   s1");
        result.ShouldContain("2. T2\n   https://b.test\n   s2");
    }

    [Fact]
    public void Dado_JsonComEntries_Quando_Formatar_Entao_UmaPorLinha()
    {
        var result = ToolResultFormatter.FormatForTerminal(
            """{"entries":["a.txt","b.txt"]}""");

        result.ShouldBe("a.txt\nb.txt");
    }

    [Fact]
    public void Dado_JsonComObjetoAninhado_Quando_Formatar_Entao_PrettyJson()
    {
        var result = ToolResultFormatter.FormatForTerminal(
            """{"meta":{"a":1}}""");

        result.ShouldContain("meta:");
        result.ShouldContain("\"a\": 1");
    }
}
