using Shouldly;
using Taskboard.Rendering;
using Xunit;

namespace Taskboard.Tests.Unit.Rendering;

/// <summary>SPEC-20260918-kanban-card-ux RF-009 / AC-5/AC-6.</summary>
public class MarkdownRendererTests
{
    [Fact]
    public void Dado_Heading_Quando_Renderizar_Entao_GeraH2()
    {
        MarkdownRenderer.ToSafeHtml("## Título").ShouldContain("<h2");
    }

    [Fact]
    public void Dado_ListaEBoldECodigo_Quando_Renderizar_Entao_Formata()
    {
        var html = MarkdownRenderer.ToSafeHtml("- **item**\n`code`");

        html.ShouldContain("<ul>");
        html.ShouldContain("<strong>item</strong>");
        html.ShouldContain("<code>code</code>");
    }

    [Fact]
    public void Dado_TaskList_Quando_Renderizar_Entao_GeraCheckboxDesabilitado()
    {
        var html = MarkdownRenderer.ToSafeHtml("- [ ] pendente\n- [x] feito");

        html.ShouldContain("type=\"checkbox\"");
        html.ShouldContain("disabled");
    }

    [Fact]
    public void Dado_ScriptTag_Quando_Renderizar_Entao_RemoveScript()
    {
        var html = MarkdownRenderer.ToSafeHtml("<script>alert(1)</script>texto");

        html.ShouldNotContain("<script");
        html.ShouldNotContain("alert(1)");
    }

    [Fact]
    public void Dado_LinkJavascript_Quando_Renderizar_Entao_NeutralizaHref()
    {
        var html = MarkdownRenderer.ToSafeHtml("[x](javascript:alert(1))");

        html.ShouldNotContain("javascript:");
    }

    [Fact]
    public void Dado_OnClickInline_Quando_Renderizar_Entao_RemoveHandler()
    {
        var html = MarkdownRenderer.ToSafeHtml("<a href=\"https://ok\" onclick=\"alert(1)\">x</a>");

        html.ShouldNotContain("onclick");
    }

    [Fact]
    public void Dado_LinkHttps_Quando_Renderizar_Entao_Preserva()
    {
        var html = MarkdownRenderer.ToSafeHtml("[ok](https://example.com)");

        html.ShouldContain("href=\"https://example.com\"");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Dado_EntradaVazia_Quando_Renderizar_Entao_RetornaVazio(string? input)
    {
        MarkdownRenderer.ToSafeHtml(input).ShouldBeEmpty();
    }
}
