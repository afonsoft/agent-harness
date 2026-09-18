using Shouldly;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

public class AgentPromptTemplateTests
{
    [Fact]
    public void Dado_TemplateComPlaceholders_Quando_Renderizar_Entao_SubstituiTodos()
    {
        // Covers RF-004: placeholders {repoUrl} {issueTitle} {issueBody}.
        var rendered = AgentPromptTemplate.Render(
            "Clone {repoUrl} e corrija: {issueTitle}\n{issueBody}",
            "https://github.com/owner/repo.git",
            "Bump Npgsql",
            "Body aqui");

        rendered.ShouldBe("Clone https://github.com/owner/repo.git e corrija: Bump Npgsql\nBody aqui");
    }

    [Fact]
    public void Dado_ValoresNulos_Quando_Renderizar_Entao_SubstituiPorVazio()
    {
        var rendered = AgentPromptTemplate.Render("{repoUrl}|{issueTitle}|{issueBody}", null, null, null);

        rendered.ShouldBe("||");
    }

    [Fact]
    public void Dado_Builtin_Quando_Renderizar_Entao_ContemInstrucoesDeCloneESkillsEMcp()
    {
        var rendered = AgentPromptTemplate.Render(
            AgentPromptTemplate.Builtin, "https://github.com/o/r.git", "T", "B");

        rendered.ShouldContain("https://github.com/o/r.git");
        rendered.ShouldContain("skills");
        rendered.ShouldContain("knowledge");
    }

    [Fact]
    public void Dado_Builtin_Quando_Renderizar_Entao_InstruiOrchestratorManageTaskboardERepos()
    {
        // SPEC-20260918-orchestrator-default-prompt RF-001: a skill orchestrator
        // gerencia o processo; clone em ~/repos; manage-taskboard movimenta o card.
        var rendered = AgentPromptTemplate.Render(
            AgentPromptTemplate.Builtin, "u", "t", "b");

        rendered.ShouldContain("orchestrator");
        rendered.ShouldContain("~/repos");
        rendered.ShouldContain("manage-taskboard");
        rendered.ShouldContain("Issue: t");
        rendered.ShouldContain("b");
    }

    [Fact]
    public void Dado_TemplateComIssueComments_Quando_Renderizar_Entao_SubstituiComentarios()
    {
        var rendered = AgentPromptTemplate.Render(
            "Issue: {issueTitle}\n{issueBody}\n\n{issueComments}",
            "u", "T", "B", "Comments:\n- dev (2026-09-18): handoff note");

        rendered.ShouldBe("Issue: T\nB\n\nComments:\n- dev (2026-09-18): handoff note");
    }

    [Fact]
    public void Dado_TemplateComIssueComments_Quando_SemComentarios_Entao_PlaceholderVazio()
    {
        var rendered = AgentPromptTemplate.Render(
            "{issueBody}\n\n{issueComments}", "u", "T", "B", null);

        rendered.ShouldBe("B");
    }

    [Fact]
    public void Dado_Builtin_Quando_Renderizar_Entao_TerminaComPlaceholderDeComentarios()
    {
        AgentPromptTemplate.Builtin.TrimEnd().ShouldEndWith("{issueComments}");
        AgentPromptTemplate.HasCommentsPlaceholder(AgentPromptTemplate.Builtin).ShouldBeTrue();
    }
}

public class AgentCliInvocationTests
{
    [Theory]
    [InlineData(AgentType.Devin, "devin --respect-workspace-trust false --model swe -p <prompt>")]
    [InlineData(AgentType.Claude, "claude --dangerously-skip-permissions --model sonnet -p <prompt>")]
    [InlineData(AgentType.Codex, "codex exec --approve-for-me --skip-git-repo-check -m gpt-5.6-luna <prompt>")]
    [InlineData(AgentType.OpenCode, "opencode run --auto -m opencode/claude-sonnet-5 <prompt>")]
    [InlineData(AgentType.Antigravity, "agy --dangerously-skip-permissions --model gemini-3.1-pro-low -p <prompt>")]
    [InlineData(AgentType.Kimi, "kimi --model kimi-k2 -p <prompt>")]
    [InlineData(AgentType.Grok, "grok --model grok-4 -p <prompt>")]
    [InlineData(AgentType.Aider, "aider --yes-always --model sonnet --message <prompt>")]
    [InlineData(AgentType.Cline, "cline <prompt>")]
    [InlineData(AgentType.Continue, "cn --auto -p <prompt>")]
    [InlineData(AgentType.Copilot, "copilot --allow-all-tools --model claude-sonnet-4-5 -p <prompt>")]
    [InlineData(AgentType.Qwen, "qwen -m qwen3-coder-plus -p <prompt>")]
    [InlineData(AgentType.Kiro, "kiro-cli chat --no-interactive --trust-all-tools <prompt>")]
    public void Dado_Agente_Quando_Preview_Entao_ComandoEsperado(AgentType type, string expected)
    {
        // Covers RF-003: preview espelha o template real do adapter.
        // Default tier = Normal (SPEC-20260918-agent-model-tiers RF-002).
        AgentCliInvocation.PreviewCommandLine(type).ShouldBe(expected);
    }

    [Fact]
    public void Dado_TodosOsTiposComCli_Quando_BuildArguments_Entao_PromptEhUltimoArgumento()
    {
        foreach (var kind in Enum.GetValues<AgentCliKind>())
        {
            var type = AgentCliMap.AgentTypeFor(kind)!.Value;
            foreach (var tier in Enum.GetValues<AgentModelTier>())
            {
                var args = AgentCliInvocation.BuildArguments(type, "P", tier);
                args.Last().ShouldBe("P", $"{type} deve receber o prompt como último argumento");
            }
        }
    }
}

public class AgentCliModelTests
{
    [Theory]
    [InlineData(AgentType.Claude, AgentModelTier.Lite, "haiku")]
    [InlineData(AgentType.Claude, AgentModelTier.Normal, "sonnet")]
    [InlineData(AgentType.Claude, AgentModelTier.Ultra, "opus")]
    [InlineData(AgentType.Devin, AgentModelTier.Normal, "swe")]
    [InlineData(AgentType.OpenCode, AgentModelTier.Ultra, "opencode/claude-opus-5")]
    public void Dado_CliComTabela_Quando_ModelFor_Entao_RetornaModelo(AgentType type, AgentModelTier tier, string expected)
    {
        AgentCliModels.ModelFor(type, tier).ShouldBe(expected);
    }

    [Theory]
    [InlineData(AgentType.Cline)]
    [InlineData(AgentType.Continue)]
    [InlineData(AgentType.Kiro)]
    [InlineData(AgentType.OpenHands)]
    public void Dado_CliSemFlag_Quando_ModelFor_Entao_Null(AgentType type)
    {
        // CLIs sem flag de modelo headless ficam "gerenciados pela CLI" — nunca recebem --model.
        AgentCliModels.SupportsModelSelection(type).ShouldBeFalse();
        AgentCliModels.ModelFor(type, AgentModelTier.Ultra).ShouldBeNull();
    }

    [Theory]
    [InlineData(AgentType.Claude, AgentModelTier.Lite, "--model", "haiku")]
    [InlineData(AgentType.Claude, AgentModelTier.Ultra, "--model", "opus")]
    [InlineData(AgentType.Codex, AgentModelTier.Lite, "-m", "gpt-5.6-luna")]
    [InlineData(AgentType.Codex, AgentModelTier.Ultra, "-m", "gpt-5.6-luna")]
    public void Dado_Tier_Quando_BuildArguments_Entao_ArgvContemFlagEModelo(
        AgentType type, AgentModelTier tier, string flag, string model)
    {
        var args = AgentCliInvocation.BuildArguments(type, "P", tier);
        var flagIndex = args.ToList().IndexOf(flag);
        flagIndex.ShouldBeGreaterThanOrEqualTo(0);
        args[flagIndex + 1].ShouldBe(model);
    }

    [Theory]
    [InlineData(AgentType.Cline)]
    [InlineData(AgentType.Continue)]
    [InlineData(AgentType.Kiro)]
    public void Dado_CliGerenciado_Quando_BuildArguments_Entao_ArgvInalterado(AgentType type)
    {
        // SPEC RF-002: CLI-managed nunca recebe flag de modelo, em qualquer tier.
        foreach (var tier in Enum.GetValues<AgentModelTier>())
        {
            var args = AgentCliInvocation.BuildArguments(type, "P", tier);
            args.ShouldNotContain("--model");
            args.ShouldNotContain("-m");
        }
    }

    [Fact]
    public void Dado_ModeloResolvido_Quando_BuildArguments_Entao_ExplicitoVenceCurado()
    {
        // SPEC-20260918-agent-model-config RF-002: o nome resolvido pela
        // orquestração (override ?? curado) vence a tabela estática.
        var args = AgentCliInvocation.BuildArguments(
            AgentType.Claude, "P", AgentModelTier.Normal, "custom-model-x");

        var flagIndex = args.ToList().IndexOf("--model");
        flagIndex.ShouldBeGreaterThanOrEqualTo(0);
        args[flagIndex + 1].ShouldBe("custom-model-x");
    }

    [Fact]
    public void Dado_ModeloResolvidoEmCliGerenciada_Quando_BuildArguments_Entao_SemFlag()
    {
        // CLI-managed nunca recebe flag, mesmo com nome explícito.
        var args = AgentCliInvocation.BuildArguments(
            AgentType.Cline, "P", AgentModelTier.Ultra, "custom-model-x");

        args.ShouldNotContain("--model");
        args.ShouldNotContain("custom-model-x");
    }
}
