using System.Text.Json;
using Shouldly;
using Taskboard.Application.Chat;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261001-ai-chat-openwebui RF-001: markup de tool call inline
/// (<｜DSML｜function_calls fullwidth/ASCII e &lt;tool_call&gt;) nunca chega à
/// UI nem persiste — é filtrado no stream e materializado como tool call real.
/// </summary>
public sealed class InlineToolCallMarkupTests
{
    private const string DsmlFullwidth =
        "<｜DSML｜function_calls><｜DSML｜invoke name=\"echo_tool\">"
        + "<｜DSML｜parameter name=\"text\" string=\"true\">rode</｜DSML｜parameter>"
        + "</｜DSML｜invoke></｜DSML｜function_calls>";

    private const string DsmlAscii =
        "<|DSML|function_calls><|DSML|invoke name=\"echo_tool\">"
        + "<|DSML|parameter name=\"text\" string=\"true\">rode</|DSML|parameter>"
        + "</|DSML|invoke></|DSML|function_calls>";

    [Fact]
    public void Dado_TextoSimples_Quando_Feed_Entao_VisivelIgual()
    {
        var filter = new InlineToolCallMarkup();

        filter.Feed("ola mundo").ShouldBe("ola mundo");
        filter.Feed("!").ShouldBe("!");
        filter.Flush().ShouldBe(string.Empty);
        filter.MaterializeCalls().ShouldBeEmpty();
    }

    [Fact]
    public void Dado_DsmlFullwidth_Quando_Feed_Entao_MarkupOcultoEChamadaMaterializada()
    {
        var filter = new InlineToolCallMarkup();

        filter.Feed(DsmlFullwidth).ShouldBe(string.Empty);
        filter.Flush().ShouldBe(string.Empty);

        var calls = filter.MaterializeCalls();
        var call = calls.ShouldHaveSingleItem();
        call.Name.ShouldBe("echo_tool");
        using var args = JsonDocument.Parse(call.ArgumentsJson);
        args.RootElement.GetProperty("text").GetString().ShouldBe("rode");
    }

    [Fact]
    public void Dado_DsmlAscii_Quando_Feed_Entao_MarkupOcultoEChamadaMaterializada()
    {
        var filter = new InlineToolCallMarkup();

        filter.Feed(DsmlAscii).ShouldBe(string.Empty);

        filter.MaterializeCalls().ShouldHaveSingleItem().Name.ShouldBe("echo_tool");
    }

    [Fact]
    public void Dado_DsmlQuebradoEmDeltas_Quando_Feed_Entao_MarkupNuncaVaza()
    {
        var filter = new InlineToolCallMarkup();
        var full = $"antes {DsmlFullwidth} depois";
        var visible = string.Empty;

        // Chunk de 7 chars — cruza os limites do marker de propósito.
        for (var i = 0; i < full.Length; i += 7)
        {
            visible += filter.Feed(full.Substring(i, Math.Min(7, full.Length - i)));
        }

        visible += filter.Flush();
        visible.ShouldBe("antes  depois");
        filter.MaterializeCalls().ShouldHaveSingleItem();
    }

    [Fact]
    public void Dado_ParametroNaoString_Quando_Feed_Entao_JsonPreservado()
    {
        var markup =
            "<｜DSML｜function_calls><｜DSML｜invoke name=\"stats\">"
            + "<｜DSML｜parameter name=\"count\" string=\"false\">42</｜DSML｜parameter>"
            + "</｜DSML｜invoke></｜DSML｜function_calls>";
        var filter = new InlineToolCallMarkup();
        filter.Feed(markup);

        var call = filter.MaterializeCalls().ShouldHaveSingleItem();
        using var args = JsonDocument.Parse(call.ArgumentsJson);
        args.RootElement.GetProperty("count").GetInt32().ShouldBe(42);
    }

    [Fact]
    public void Dado_ToolCallEnvelope_Quando_Feed_Entao_ChamadaMaterializada()
    {
        var filter = new InlineToolCallMarkup();

        filter.Feed("""<tool_call>{"name":"functions.web_search","arguments":{"query":"x"}}</tool_call>""")
            .ShouldBe(string.Empty);

        var call = filter.MaterializeCalls().ShouldHaveSingleItem();
        call.Name.ShouldBe("web_search", "namespace functions. é normalizado para o último segmento");
        using var args = JsonDocument.Parse(call.ArgumentsJson);
        args.RootElement.GetProperty("query").GetString().ShouldBe("x");
    }

    [Fact]
    public void Dado_BlocoIncompleto_Quando_Flush_Entao_MarkupDescartadoSemVazar()
    {
        var filter = new InlineToolCallMarkup();

        filter.Feed("texto <｜DSML｜function_calls><｜DSML｜invoke name=\"x\"")
            .ShouldBe("texto ");
        filter.Flush().ShouldBe(string.Empty);
        filter.MaterializeCalls().ShouldBeEmpty();
    }

    [Fact]
    public void Dado_ConteudoPersistido_Quando_StripBlocks_Entao_MarkupRemovido()
    {
        InlineToolCallMarkup.StripBlocks($"resposta {DsmlFullwidth} fim")
            .ShouldBe("resposta  fim");
    }

    [Fact]
    public void Dado_ConteudoSemMarkup_Quando_StripBlocks_Entao_Inalterado()
    {
        const string plain = "resposta normal <tag> com < caracteres";
        InlineToolCallMarkup.StripBlocks(plain).ShouldBe(plain);
    }

    [Fact]
    public void Dado_ConteudoPersistido_Quando_ExtractCalls_Entao_ChamadaRecuperada()
    {
        var calls = InlineToolCallMarkup.ExtractCalls($"vou rodar {DsmlFullwidth}");

        calls.ShouldHaveSingleItem().Name.ShouldBe("echo_tool");
    }
}
