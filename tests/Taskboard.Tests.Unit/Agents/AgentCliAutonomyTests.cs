using Shouldly;
using Taskboard.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>SPEC-20261022-cli-autonomy-profiles RF-001/003/006: per-CLI
/// autonomy flag table, the full-isolation gate, and the implied-level
/// classification used by the ExtraAgentArgs whitelist.</summary>
public sealed class AgentCliAutonomyTests
{
    [Fact]
    public void Dado_NivelEdit_Quando_Flags_Entao_ReproduzArgvHistorico()
    {
        AgentCliAutonomy.Flags(AgentType.Claude, AgentAutonomy.Edit)
            .ShouldBe(["--dangerously-skip-permissions"]);
        AgentCliAutonomy.Flags(AgentType.Codex, AgentAutonomy.Edit)
            .ShouldBe(["exec", "--approve-for-me", "--skip-git-repo-check"]);
        AgentCliAutonomy.Flags(AgentType.Kiro, AgentAutonomy.Edit)
            .ShouldBe(["chat", "--no-interactive", "--trust-all-tools"]);
    }

    [Fact]
    public void Dado_NivelPlan_Quando_Flags_Entao_DowngradeReadOnly()
    {
        AgentCliAutonomy.Flags(AgentType.Claude, AgentAutonomy.Plan)
            .ShouldBe(["--permission-mode", "dontAsk"]);
        AgentCliAutonomy.Flags(AgentType.Codex, AgentAutonomy.Plan)
            .ShouldBe(["exec", "--sandbox", "read-only", "--skip-git-repo-check"]);
        AgentCliAutonomy.Flags(AgentType.OpenCode, AgentAutonomy.Plan)
            .ShouldBe(["run"]);
    }

    [Fact]
    public void Dado_NivelFull_Quando_Flags_Entao_MaximoVerificado()
    {
        AgentCliAutonomy.Flags(AgentType.Codex, AgentAutonomy.Full)
            .ShouldBe(["exec", "--dangerously-bypass-approvals-and-sandbox", "--skip-git-repo-check"]);
    }

    [Fact]
    public void Dado_CliSemFlags_Quando_Plan_Entao_Advisory()
    {
        AgentCliAutonomy.IsAdvisory(AgentType.Kimi, AgentAutonomy.Plan).ShouldBeTrue();
        AgentCliAutonomy.IsAdvisory(AgentType.Aider, AgentAutonomy.Plan).ShouldBeTrue();
        AgentCliAutonomy.IsAdvisory(AgentType.Claude, AgentAutonomy.Plan).ShouldBeFalse();
    }

    [Theory]
    [InlineData(false, false, "autonomy_full_disabled")]
    [InlineData(true, false, "autonomy_requires_isolation")]
    [InlineData(true, true, null)]
    public void Dado_Full_Quando_GateError_Entao_ExigeIsolamentoEOptIn(
        bool allowFull, bool isolated, string? expected)
    {
        AgentCliAutonomy.GateError(AgentAutonomy.Full, isolated, allowFull)
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData(AgentAutonomy.Plan)]
    [InlineData(AgentAutonomy.Edit)]
    public void Dado_NivelNaoFull_Quando_GateError_Entao_SemprePermite(AgentAutonomy level)
    {
        AgentCliAutonomy.GateError(level, isIsolated: false, allowFull: false)
            .ShouldBeNull();
    }

    [Theory]
    [InlineData(AgentType.Claude, "--dangerously-skip-permissions", null, AgentAutonomy.Full)]
    [InlineData(AgentType.Claude, "--permission-mode", "dontAsk", AgentAutonomy.Plan)]
    [InlineData(AgentType.Claude, "--permission-mode", "acceptEdits", AgentAutonomy.Edit)]
    [InlineData(AgentType.Claude, "--permission-mode", "bypassPermissions", AgentAutonomy.Full)]
    [InlineData(AgentType.Codex, "--sandbox", "read-only", AgentAutonomy.Plan)]
    [InlineData(AgentType.Codex, "--sandbox", "workspace-write", AgentAutonomy.Edit)]
    [InlineData(AgentType.Codex, "--yolo", null, AgentAutonomy.Full)]
    [InlineData(AgentType.Codex, "--approve-for-me", null, AgentAutonomy.Edit)]
    [InlineData(AgentType.Devin, "--permission-mode", "auto", AgentAutonomy.Plan)]
    [InlineData(AgentType.Devin, "--permission-mode", "dangerous", AgentAutonomy.Full)]
    [InlineData(AgentType.OpenCode, "--auto", null, AgentAutonomy.Edit)]
    [InlineData(AgentType.Copilot, "--allow-all-tools", null, AgentAutonomy.Edit)]
    [InlineData(AgentType.Kiro, "--trust-all-tools", null, AgentAutonomy.Edit)]
    public void Dado_FlagAutonomia_Quando_FlagLevel_Entao_Classifica(
        AgentType cli, string flag, string? value, AgentAutonomy expected)
    {
        AgentCliAutonomy.FlagLevel(cli, flag, value).ShouldBe(expected);
    }

    [Theory]
    [InlineData(AgentType.Claude, "--model")]
    [InlineData(AgentType.Claude, "--unknown-flag")]
    [InlineData(AgentType.Claude, "--permission-mode", "bogus")]
    [InlineData(AgentType.Kimi, "--auto")]
    public void Dado_FlagNaoAutonomia_Quando_FlagLevel_Entao_Nulo(
        AgentType cli, string flag, string? value = null)
    {
        AgentCliAutonomy.FlagLevel(cli, flag, value).ShouldBeNull();
    }

    [Fact]
    public void Dado_ExtrasComFlagNivelEdit_Quando_MaxEdit_Entao_PassaVerbatim()
    {
        var parsed = AgentExtraArgs.Parse("--auto", "opencode", AgentAutonomy.Edit);

        parsed.Error.ShouldBeNull();
        AgentExtraArgs.PassthroughArgv(parsed).ShouldBe(["--auto"]);
    }

    [Fact]
    public void Dado_ExtrasComFlagFull_Quando_MaxEdit_Entao_Rejeita()
    {
        var parsed = AgentExtraArgs.Parse("--yolo", "codex", AgentAutonomy.Edit);

        parsed.Error.ShouldNotBeNull().ShouldContain("exceeds autonomy 'edit'");
    }

    [Fact]
    public void Dado_ExtrasComFlagFull_Quando_MaxFull_Entao_Passa()
    {
        var parsed = AgentExtraArgs.Parse("--yolo", "codex", AgentAutonomy.Full);

        parsed.Error.ShouldBeNull();
        AgentExtraArgs.PassthroughArgv(parsed).ShouldBe(["--yolo"]);
    }

    [Fact]
    public void Dado_ExtrasComSandbox_Quando_MaxPlan_Entao_AceitaReadOnly()
    {
        var parsed = AgentExtraArgs.Parse("--sandbox read-only", "codex", AgentAutonomy.Plan);

        parsed.Error.ShouldBeNull();
        AgentExtraArgs.PassthroughArgv(parsed).ShouldBe(["--sandbox", "read-only"]);
    }

    [Fact]
    public void Dado_ExtrasComSandboxWrite_Quando_MaxPlan_Entao_Rejeita()
    {
        var parsed = AgentExtraArgs.Parse("--sandbox workspace-write", "codex", AgentAutonomy.Plan);

        parsed.Error.ShouldNotBeNull().ShouldContain("exceeds autonomy 'plan'");
    }
}
