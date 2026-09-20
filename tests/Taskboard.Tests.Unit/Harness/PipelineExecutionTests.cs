using Shouldly;
using Taskboard.Agents;
using Taskboard.Domain.Entities.Harness;
using Taskboard.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

public class PipelineExecutionTests
{
    private static readonly DateTime Now = new(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);

    private static PipelineDefinition StandardFeature() =>
        new("standard-feature", "Standard Feature",
        [
            new PipelineStage("architect", "Architect", PipelineStageKind.AgentWork,
                AgentRole.Architect, AgentType.Claude, AgentModelTier.Ultra, []),
            new PipelineStage("approve-plan", "Approval", PipelineStageKind.Approval,
                null, null, AgentModelTier.Normal, ["architect"]),
            new PipelineStage("builder", "Builder", PipelineStageKind.AgentWork,
                AgentRole.Builder, AgentType.OpenCode, AgentModelTier.Normal, ["approve-plan"]),
            new PipelineStage("verifier", "Verifier", PipelineStageKind.Verification,
                null, null, AgentModelTier.Normal, ["builder"]),
            new PipelineStage("reviewer", "Reviewer", PipelineStageKind.AgentWork,
                AgentRole.Reviewer, AgentType.Devin, AgentModelTier.Normal, ["verifier"]),
        ]);

    private static PipelineExecution CriarExecucao(PipelineDefinition? def = null) =>
        PipelineExecution.Create(
            def ?? StandardFeature(), "afonsoft/taskboard-ai", "main",
            issueId: "150", initialPrompt: "Implementar JWT", Now);

    [Fact]
    public void Dado_TemplateValido_Quando_Criar_Entao_EstagiosPendentesEExecucaoRunning()
    {
        var exec = CriarExecucao();

        exec.Status.ShouldBe(PipelineStatus.Running);
        exec.Stages.Count.ShouldBe(5);
        exec.Stages.ShouldAllBe(s => s.Status == StageStatus.Pending);
        exec.Stages.Select(s => s.StageKey).ShouldBe(
            ["architect", "approve-plan", "builder", "verifier", "reviewer"]);
        exec.Stages.Single(s => s.StageKey == "builder").DependsOn.ShouldBe(["approve-plan"]);
    }

    [Fact]
    public void Dado_ExecucaoNova_Quando_Elegiveis_Entao_SomenteSemDependencia()
    {
        var exec = CriarExecucao();

        exec.EligibleStages().ShouldHaveSingleItem().StageKey.ShouldBe("architect");
    }

    [Fact]
    public void Dado_ArchitectCompleto_Quando_Elegiveis_Entao_GateDeAprovacao()
    {
        var exec = CriarExecucao();
        exec.MarkStageRunning("architect", Now);
        exec.CompleteStage("architect", "plano.md gerado", Now);

        var eligible = exec.EligibleStages().ShouldHaveSingleItem();
        eligible.StageKey.ShouldBe("approve-plan");
        eligible.Kind.ShouldBe(PipelineStageKind.Approval);
    }

    [Fact]
    public void Dado_GateEmEspera_Quando_Aprovar_Entao_CompletaEProximoElegivel()
    {
        var exec = CriarExecucao();
        exec.MarkStageRunning("architect", Now);
        exec.CompleteStage("architect", "plano", Now);
        exec.MarkStageWaitingApproval("approve-plan");

        exec.Status.ShouldBe(PipelineStatus.WaitingApproval);
        exec.ApproveStage("approve-plan", "aprovado", Now);

        exec.Status.ShouldBe(PipelineStatus.Running);
        exec.EligibleStages().ShouldHaveSingleItem().StageKey.ShouldBe("builder");
    }

    [Fact]
    public void Dado_AprovacaoSemEspera_Quando_Aprovar_Entao_DomainException()
    {
        var exec = CriarExecucao();

        Should.Throw<DomainException>(() => exec.ApproveStage("approve-plan", "x", Now))
            .Code.ShouldBe(TaskboardDomainErrorCodes.InvalidValue);
    }

    [Fact]
    public void Dado_EstagioFalhou_Quando_Falhar_Entao_PipelineAwaitingRetry()
    {
        var exec = CriarExecucao();
        exec.MarkStageRunning("architect", Now);
        exec.FailStage("architect", "timeout do agente", Now);

        exec.Status.ShouldBe(PipelineStatus.AwaitingRetry);
        exec.Stages[0].Status.ShouldBe(StageStatus.Failed);
        exec.Stages[0].LastError.ShouldBe("timeout do agente");
    }

    [Fact]
    public void Dado_EstagioFalho_Quando_Retry_Entao_VoltaParaPendenteEIncrementaTentativa()
    {
        var exec = CriarExecucao();
        exec.MarkStageRunning("architect", Now);
        exec.FailStage("architect", "boom", Now);

        exec.RetryStage("architect", "prompt ajustado", Now);

        var stage = exec.Stages[0];
        stage.Status.ShouldBe(StageStatus.Pending);
        stage.Attempts.ShouldBe(2);
        stage.AdjustedPrompt.ShouldBe("prompt ajustado");
        exec.Status.ShouldBe(PipelineStatus.Running);
        exec.EligibleStages().ShouldHaveSingleItem().StageKey.ShouldBe("architect");
    }

    [Fact]
    public void Dado_TodosEstagiosCompletos_Quando_UltimoCompleta_Entao_PipelineCompleted()
    {
        var exec = CriarExecucao();
        exec.MarkStageRunning("architect", Now);
        exec.CompleteStage("architect", "plano", Now);
        exec.MarkStageWaitingApproval("approve-plan");
        exec.ApproveStage("approve-plan", "ok", Now);
        exec.MarkStageRunning("builder", Now);
        exec.CompleteStage("builder", "codigo", Now);
        exec.MarkStageRunning("verifier", Now);
        exec.CompleteStage("verifier", "verde", Now);
        exec.MarkStageRunning("reviewer", Now);
        exec.CompleteStage("reviewer", "lgtm", Now);

        exec.Status.ShouldBe(PipelineStatus.Completed);
        exec.CompletedAtUtc.ShouldBe(Now);
    }

    [Fact]
    public void Dado_EstagiosParalelos_Quando_DependenciaCompleta_Entao_AmbosElegiveis()
    {
        var def = new PipelineDefinition("par", "Paralelo",
        [
            new PipelineStage("plan", "Plan", PipelineStageKind.AgentWork,
                AgentRole.Architect, AgentType.Claude, AgentModelTier.Normal, []),
            new PipelineStage("front", "Frontend", PipelineStageKind.AgentWork,
                AgentRole.Builder, AgentType.Codex, AgentModelTier.Normal, ["plan"]),
            new PipelineStage("back", "Backend", PipelineStageKind.AgentWork,
                AgentRole.Builder, AgentType.OpenCode, AgentModelTier.Normal, ["plan"]),
        ]);
        var exec = CriarExecucao(def);
        exec.MarkStageRunning("plan", Now);
        exec.CompleteStage("plan", "ok", Now);

        exec.EligibleStages().Select(s => s.StageKey).ShouldBe(["front", "back"]);
    }

    [Fact]
    public void Dado_CicloNoGrafo_Quando_Criar_Entao_InvalidPipelineDag()
    {
        var ciclico = new PipelineDefinition("ciclo", "Ciclo",
        [
            new PipelineStage("a", "A", PipelineStageKind.AgentWork,
                AgentRole.Builder, AgentType.Codex, AgentModelTier.Normal, ["b"]),
            new PipelineStage("b", "B", PipelineStageKind.AgentWork,
                AgentRole.Builder, AgentType.Codex, AgentModelTier.Normal, ["a"]),
        ]);

        Should.Throw<DomainException>(() => CriarExecucao(ciclico))
            .Code.ShouldBe(TaskboardDomainErrorCodes.InvalidPipelineDag);
    }

    [Fact]
    public void Dado_DependenciaInexistente_Quando_Criar_Entao_InvalidPipelineDag()
    {
        var quebrado = new PipelineDefinition("quebrado", "Quebrado",
        [
            new PipelineStage("a", "A", PipelineStageKind.AgentWork,
                AgentRole.Builder, AgentType.Codex, AgentModelTier.Normal, ["fantasma"]),
        ]);

        Should.Throw<DomainException>(() => CriarExecucao(quebrado))
            .Code.ShouldBe(TaskboardDomainErrorCodes.InvalidPipelineDag);
    }

    [Fact]
    public void Dado_PipelineEmAndamento_Quando_Cancelar_Entao_EstagiosAtivosSkipped()
    {
        var exec = CriarExecucao();
        exec.MarkStageRunning("architect", Now);

        exec.Cancel(Now);

        exec.Status.ShouldBe(PipelineStatus.Cancelled);
        exec.Stages[0].Status.ShouldBe(StageStatus.Skipped);
        exec.Stages.Skip(1).ShouldAllBe(s => s.Status == StageStatus.Skipped);
    }
}
