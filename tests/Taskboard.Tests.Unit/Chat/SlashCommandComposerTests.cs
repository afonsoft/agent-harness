using Shouldly;
using Taskboard.Application.Contracts.Chat;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261004-cli-slash-commands RF-003: composição do turno para comandos
/// de CLI — marker <c>[command:name]</c> + bloco <c>&lt;command&gt;</c>, com
/// substituição de <c>$ARGUMENTS</c> (convenção claude/opencode).
/// </summary>
public sealed class SlashCommandComposerTests
{
    [Fact]
    public void Dado_BodyComArguments_Quando_Compose_Entao_SubstituiArgs()
    {
        var composed = SlashCommandComposer.ComposeCliCommand("review", "claude", "Review $ARGUMENTS now.", "main");

        composed.ShouldStartWith("[command:review] main");
        composed.ShouldContain("<command name=\"review\" source=\"claude\">");
        composed.ShouldContain("Review main now.");
        composed.ShouldNotContain("$ARGUMENTS");
    }

    [Fact]
    public void Dado_BodySemPlaceholder_Quando_ComposeComArgs_Entao_AnexaArgs()
    {
        var composed = SlashCommandComposer.ComposeCliCommand("plan", "opencode", "Draft the plan.", "the auth feature");

        composed.ShouldContain("Draft the plan.");
        composed.ShouldContain("Arguments: the auth feature");
    }

    [Fact]
    public void Dado_SemArgs_Quando_Compose_Entao_SemSufixo()
    {
        var composed = SlashCommandComposer.ComposeCliCommand("init", "opencode", "Bootstrap AGENTS.md.", null);

        composed.ShouldStartWith("[command:init]");
        composed.ShouldContain("Bootstrap AGENTS.md.");
        composed.ShouldNotContain("Arguments:");
    }

    [Fact]
    public void Dado_SkillDeCli_Quando_Compose_Entao_UsaMarkerSkill()
    {
        var composed = SlashCommandComposer.ComposeCliCommand("deploy", "claude", "Deploy steps.", null, kind: "skill");

        composed.ShouldStartWith("[skill:deploy]");
        composed.ShouldContain("<skill name=\"deploy\" source=\"claude\">");
    }
}
