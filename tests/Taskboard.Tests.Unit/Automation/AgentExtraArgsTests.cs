using Shouldly;
using Taskboard.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Automation;

/// <summary>SPEC-20261021-agent-automations RF-005: extra-agent-args
/// whitelist — model/effort map to launch prefs, --add-dir passes through
/// for claude-family, everything else is refused.</summary>
public sealed class AgentExtraArgsTests
{
    [Fact]
    public void Dado_ArgsNulos_Quando_Parse_Entao_ResultadoVazio()
    {
        var parsed = AgentExtraArgs.Parse(null, "claude");

        parsed.Error.ShouldBeNull();
        parsed.Model.ShouldBeNull();
        parsed.Effort.ShouldBeNull();
        parsed.AddDirs.ShouldBeEmpty();
    }

    [Fact]
    public void Dado_ModelEEffort_Quando_Parse_Entao_ExtraiPreferencias()
    {
        var parsed = AgentExtraArgs.Parse("--model opus --effort high", "claude");

        parsed.Error.ShouldBeNull();
        parsed.Model.ShouldBe("opus");
        parsed.Effort.ShouldBe("high");
    }

    [Fact]
    public void Dado_FormatoIgual_Quando_Parse_Entao_ExtraiValores()
    {
        var parsed = AgentExtraArgs.Parse("--model=gpt-5 --effort=low", "codex");

        parsed.Error.ShouldBeNull();
        parsed.Model.ShouldBe("gpt-5");
        parsed.Effort.ShouldBe("low");
    }

    [Fact]
    public void Dado_AddDir_Quando_CliClaude_Entao_Aceita()
    {
        var parsed = AgentExtraArgs.Parse("--add-dir /tmp/extra", "claude");

        parsed.Error.ShouldBeNull();
        parsed.AddDirs.ShouldBe(["/tmp/extra"]);
        AgentExtraArgs.PassthroughArgv(parsed)
            .ShouldBe(["--add-dir", "/tmp/extra"]);
    }

    [Fact]
    public void Dado_AddDirRelativo_Quando_Parse_Entao_Rejeita()
    {
        var parsed = AgentExtraArgs.Parse("--add-dir ../x", "claude");

        parsed.Error.ShouldNotBeNull().ShouldContain("absolute path");
    }

    [Fact]
    public void Dado_AddDir_Quando_CliNaoClaude_Entao_Rejeita()
    {
        var parsed = AgentExtraArgs.Parse("--add-dir /tmp/x", "codex");

        parsed.Error.ShouldNotBeNull().ShouldContain("unsupported");
    }

    [Theory]
    [InlineData("--permission-mode dangerous", "claude")]
    [InlineData("--dangerously-skip-permissions", "claude")]
    [InlineData("--config x=y", "codex")]
    [InlineData("-p \"do bad\"", "claude")]
    [InlineData("--print test", "claude")]
    [InlineData("--session-id abc", "claude")]
    [InlineData("--resume abc", "claude")]
    [InlineData("--continue", "claude")]
    [InlineData("--mcp-config /tmp/mcp.json", "claude")]
    [InlineData("--output-format json", "claude")]
    [InlineData("--verbose", "codex")]
    public void Dado_FlagProibida_Quando_Parse_Entao_InvalidExtraArgs(string raw, string cli)
    {
        var parsed = AgentExtraArgs.Parse(raw, cli);

        parsed.Error.ShouldNotBeNull().ShouldContain("invalid_extra_args");
    }

    [Fact]
    public void Dado_ValorSemFlag_Quando_Parse_Entao_Rejeita()
    {
        var parsed = AgentExtraArgs.Parse("bareword", "claude");

        parsed.Error.ShouldNotBeNull().ShouldContain("unexpected value");
    }

    [Fact]
    public void Dado_FlagSemValor_Quando_Parse_Entao_Rejeita()
    {
        var parsed = AgentExtraArgs.Parse("--model", "claude");

        parsed.Error.ShouldNotBeNull().ShouldContain("requires a model value");
    }

    [Fact]
    public void Dado_EffortEmDefCustom_Quando_Parse_Entao_Rejeita()
    {
        var parsed = AgentExtraArgs.Parse("--effort high", "my-custom-cli");

        parsed.Error.ShouldNotBeNull().ShouldContain("custom cli");
    }

    [Fact]
    public void Dado_ModelEmDefCustom_Quando_Parse_Entao_Aceita()
    {
        var parsed = AgentExtraArgs.Parse("--model foo", "my-custom-cli");

        parsed.Error.ShouldBeNull();
        parsed.Model.ShouldBe("foo");
    }

    [Fact]
    public void Dado_ValorComEspaco_Quando_Parse_Entao_RespeitaAspas()
    {
        var parsed = AgentExtraArgs.Parse("--model \"gpt 5\"", "claude");

        parsed.Error.ShouldBeNull();
        parsed.Model.ShouldBe("gpt 5");
    }
}
