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
}

public class AgentCliInvocationTests
{
    [Theory]
    [InlineData(AgentType.Devin, "devin --respect-workspace-trust false -p <prompt>")]
    [InlineData(AgentType.Claude, "claude --dangerously-skip-permissions -p <prompt>")]
    [InlineData(AgentType.Codex, "codex exec --approve-for-me --skip-git-repo-check <prompt>")]
    [InlineData(AgentType.OpenCode, "opencode run <prompt>")]
    [InlineData(AgentType.Antigravity, "agy -p <prompt>")]
    [InlineData(AgentType.Kimi, "kimi -p <prompt>")]
    [InlineData(AgentType.Grok, "grok -p <prompt>")]
    [InlineData(AgentType.Aider, "aider --yes-always --message <prompt>")]
    [InlineData(AgentType.Cline, "cline <prompt>")]
    [InlineData(AgentType.Continue, "cn --auto -p <prompt>")]
    [InlineData(AgentType.Copilot, "copilot --allow-all-tools -p <prompt>")]
    [InlineData(AgentType.Qwen, "qwen -p <prompt>")]
    [InlineData(AgentType.Kiro, "kiro-cli chat --no-interactive --trust-all-tools <prompt>")]
    public void Dado_Agente_Quando_Preview_Entao_ComandoEsperado(AgentType type, string expected)
    {
        // Covers RF-003: preview espelha o template real do adapter.
        AgentCliInvocation.PreviewCommandLine(type).ShouldBe(expected);
    }

    [Fact]
    public void Dado_TodosOsTiposComCli_Quando_BuildArguments_Entao_PromptEhUltimoArgumento()
    {
        foreach (var kind in Enum.GetValues<AgentCliKind>())
        {
            var type = AgentCliMap.AgentTypeFor(kind)!.Value;
            var args = AgentCliInvocation.BuildArguments(type, "P");
            args.Last().ShouldBe("P", $"{type} deve receber o prompt como último argumento");
        }
    }
}
