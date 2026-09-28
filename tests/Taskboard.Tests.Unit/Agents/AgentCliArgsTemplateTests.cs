using Shouldly;
using Taskboard.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>SPEC-20260928-ai-code-generic-cli RF-002: argsTemplate tokenization + token rendering.</summary>
public class AgentCliArgsTemplateTests
{
    [Fact]
    public void Dado_TemplateNulo_Quando_Render_Entao_ListaVazia() =>
        AgentCliArgsTemplate.Render(null).ShouldBeEmpty();

    [Fact]
    public void Dado_TemplateSimples_Quando_Render_Entao_TokensPorEspaco()
    {
        var args = AgentCliArgsTemplate.Render("--acp --verbose");

        args.ShouldBe(["--acp", "--verbose"]);
    }

    [Fact]
    public void Dado_GrupoComAspas_Quando_Render_Entao_MantemEspacosInternos()
    {
        var args = AgentCliArgsTemplate.Render("--prompt \"fix the bug\" --fast");

        args.ShouldBe(["--prompt", "fix the bug", "--fast"]);
    }

    [Fact]
    public void Dado_TokenModel_Quando_ModeloInformado_Entao_Substitui()
    {
        var args = AgentCliArgsTemplate.Render("--model {model}", model: "gpt-5");

        args.ShouldBe(["--model", "gpt-5"]);
    }

    [Fact]
    public void Dado_SemTokenModel_Quando_ModeloEModelFlag_Entao_AcrescentaFlag()
    {
        var args = AgentCliArgsTemplate.Render("--acp", modelFlag: "--model", model: "sonnet");

        args.ShouldBe(["--acp", "--model", "sonnet"]);
    }

    [Fact]
    public void Dado_SemTokenModel_Quando_SemModelFlag_Entao_NaoAcrescenta()
    {
        var args = AgentCliArgsTemplate.Render("--acp", model: "sonnet");

        args.ShouldBe(["--acp"]);
    }

    [Fact]
    public void Dado_TokenPrompt_Quando_SemPrompt_Entao_TokenPermanece()
    {
        // Interactive PTY spawns render without a prompt — the token stays
        // verbatim so CLIs see the literal text (documented behaviour).
        var args = AgentCliArgsTemplate.Render("run {prompt}");

        args.ShouldBe(["run", "{prompt}"]);
    }

    [Fact]
    public void Dado_TokenPrompt_Quando_PromptInformado_Entao_Substitui()
    {
        var args = AgentCliArgsTemplate.Render("run {prompt}", prompt: "hello world");

        args.ShouldBe(["run", "hello world"]);
    }

    [Fact]
    public void Dado_TokenDesconhecido_Quando_Render_Entao_PermaneceVerbatim()
    {
        var args = AgentCliArgsTemplate.Render("--config {other}");

        args.ShouldBe(["--config", "{other}"]);
    }
}
