using Shouldly;
using Taskboard.Agents;
using Taskboard.Domain.Entities.Harness;
using Taskboard.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

/// <summary>
/// SPEC-20260920-cockpit-pause-resume — transições `Pause`/`Resume` do
/// <see cref="PipelineExecution"/> (AC1/AC2/AC4).
/// </summary>
public class PipelinePauseResumeTests
{
    private static readonly DateTime Now = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

    private static PipelineDefinition TwoStages() =>
        new("quick-patch", "Quick Patch",
        [
            new PipelineStage("builder", "Builder", PipelineStageKind.AgentWork,
                AgentRole.Builder, AgentType.Codex, AgentModelTier.Normal, []),
            new PipelineStage("verifier", "Verifier", PipelineStageKind.Verification,
                null, null, AgentModelTier.Normal, ["builder"]),
        ]);

    private static PipelineExecution CriarExecucao() =>
        PipelineExecution.Create(
            TwoStages(), "afonsoft/agent-harness", "/repo/taskboard", "main",
            issueId: null, initialPrompt: "Implementar JWT", Now);

    [Fact]
    public void Dado_Running_Quando_Pause_Entao_PausedESemElegiveis()
    {
        var exec = CriarExecucao();
        exec.MarkStageRunning("builder", Now);

        exec.Pause();

        exec.Status.ShouldBe(PipelineStatus.Paused);
        exec.EligibleStages().ShouldBeEmpty();
    }

    [Fact]
    public void Dado_PausedComEstagioEmVoo_Quando_CompleteStage_Entao_PermanecePaused()
    {
        var exec = CriarExecucao();
        exec.MarkStageRunning("builder", Now);
        exec.Pause();

        exec.CompleteStage("builder", "patch aplicado", Now);

        exec.Status.ShouldBe(PipelineStatus.Paused);
        exec.Stages.Single(s => s.StageKey == "builder").Status.ShouldBe(StageStatus.Completed);
        exec.EligibleStages().ShouldBeEmpty();
    }

    [Fact]
    public void Dado_Paused_Quando_Resume_Entao_RunningEDespachaPendentes()
    {
        var exec = CriarExecucao();
        exec.MarkStageRunning("builder", Now);
        exec.Pause();
        exec.CompleteStage("builder", "patch aplicado", Now);

        exec.Resume();

        exec.Status.ShouldBe(PipelineStatus.Running);
        exec.EligibleStages().ShouldHaveSingleItem().StageKey.ShouldBe("verifier");
    }

    [Fact]
    public void Dado_PausedComUltimoEstagioCompleto_Quando_Resume_Entao_Completed()
    {
        var exec = CriarExecucao();
        exec.MarkStageRunning("builder", Now);
        exec.CompleteStage("builder", "ok", Now);
        exec.MarkStageRunning("verifier", Now);
        exec.Pause();
        exec.CompleteStage("verifier", "verde", Now);

        exec.Resume();

        exec.Status.ShouldBe(PipelineStatus.Completed);
    }

    [Fact]
    public void Dado_Paused_Quando_Pause_Entao_InvalidPipelineState()
    {
        var exec = CriarExecucao();
        exec.Pause();

        var ex = Should.Throw<DomainException>(exec.Pause);
        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidPipelineState);
    }

    [Fact]
    public void Dado_Completed_Quando_Pause_Entao_InvalidPipelineState()
    {
        var exec = CriarExecucao();
        exec.MarkStageRunning("builder", Now);
        exec.CompleteStage("builder", "ok", Now);
        exec.MarkStageRunning("verifier", Now);
        exec.CompleteStage("verifier", "ok", Now);
        exec.Status.ShouldBe(PipelineStatus.Completed);

        Should.Throw<DomainException>(exec.Pause).Code
            .ShouldBe(TaskboardDomainErrorCodes.InvalidPipelineState);
    }

    [Fact]
    public void Dado_Cancelled_Quando_Pause_Entao_InvalidPipelineState()
    {
        var exec = CriarExecucao();
        exec.Cancel(Now);

        Should.Throw<DomainException>(exec.Pause).Code
            .ShouldBe(TaskboardDomainErrorCodes.InvalidPipelineState);
    }

    [Fact]
    public void Dado_Running_Quando_Resume_Entao_InvalidPipelineState()
    {
        var exec = CriarExecucao();

        Should.Throw<DomainException>(exec.Resume).Code
            .ShouldBe(TaskboardDomainErrorCodes.InvalidPipelineState);
    }

    [Fact]
    public void Dado_Paused_Quando_Cancel_Entao_Cancelled()
    {
        var exec = CriarExecucao();
        exec.Pause();

        exec.Cancel(Now);

        exec.Status.ShouldBe(PipelineStatus.Cancelled);
        exec.Stages.ShouldAllBe(s => s.Status == StageStatus.Skipped);
    }

    [Fact]
    public void Dado_WaitingApproval_Quando_PauseResume_Entao_VoltaParaWaitingApproval()
    {
        var exec = CriarExecucao();
        exec.MarkStageRunning("builder", Now);
        exec.Stages.Single(s => s.StageKey == "builder");
        // Simula gate: força WaitingApproval via definição com Approval pendente
        var gated = PipelineExecution.Create(
            new PipelineDefinition("gated", "Gated",
            [
                new PipelineStage("gate", "Approval", PipelineStageKind.Approval,
                    null, null, AgentModelTier.Normal, []),
                new PipelineStage("builder", "Builder", PipelineStageKind.AgentWork,
                    AgentRole.Builder, AgentType.Codex, AgentModelTier.Normal, ["gate"]),
            ]),
            "afonsoft/agent-harness", "/repo/taskboard", "main", null, "prompt", Now);
        gated.MarkStageWaitingApproval("gate");
        gated.Status.ShouldBe(PipelineStatus.WaitingApproval);

        gated.Pause();
        gated.Status.ShouldBe(PipelineStatus.Paused);

        gated.Resume();
        gated.Status.ShouldBe(PipelineStatus.WaitingApproval);
    }
}
