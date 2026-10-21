using Shouldly;
using Taskboard;
using Taskboard.Delegation;
using Taskboard.Domain.Entities.Delegation;
using Xunit;

namespace Taskboard.Tests.Unit.Delegation;

/// <summary>
/// SPEC-20261020-orchestration-contract-v2 (P1): invariantes de DelegationRun,
/// Dispatch (attempt do task) e Delivery (batch de mailbox com ack), mais os
/// campos novos de DelegationTask/AgentMailboxMessage.
/// </summary>
public class OrchestrationContractEntitiesTests
{
    // ---- DelegationRun (RF-001) ----

    [Fact]
    public void Dado_RunValido_Quando_Cria_Entao_OpenECampos()
    {
        var run = DelegationRun.Create("conv-1", "ship the feature", "assistant");

        run.Id.ShouldStartWith("run-");
        run.Status.ShouldBe(DelegationRunStatus.Open);
        run.Scope.ShouldBe("conv-1");
        run.Objective.ShouldBe("ship the feature");
        run.CoordinatorKey.ShouldBe("assistant");
        run.SettledAtUtc.ShouldBeNull();
    }

    [Fact]
    public void Dado_Run_Quando_SettleOuAbort_Entao_StatusFinal()
    {
        var run = DelegationRun.Create("conv-1", "obj", "assistant");
        run.Settle();
        run.Status.ShouldBe(DelegationRunStatus.Settled);
        run.SettledAtUtc.ShouldNotBeNull();

        var other = DelegationRun.Create("conv-1", "obj", "assistant");
        other.Abort();
        other.Status.ShouldBe(DelegationRunStatus.Aborted);
        other.SettledAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public void Dado_EscopoVazio_Quando_CriaRun_Entao_DefaultHarness()
    {
        DelegationRun.Create(" ", "obj", "assistant").Scope.ShouldBe("harness");
    }

    // ---- Dispatch (RF-002) ----

    [Fact]
    public void Dado_DispatchValido_Quando_Cria_Entao_PendingAttemptUm()
    {
        var dispatch = Dispatch.Create("task-1", "run-1", "codex", attempt: 1, preambleJson: "{}");

        dispatch.Id.ShouldStartWith("disp-");
        dispatch.State.ShouldBe(DispatchState.Pending);
        dispatch.Attempt.ShouldBe(1);
        dispatch.TaskId.ShouldBe("task-1");
        dispatch.RunId.ShouldBe("run-1");
        dispatch.WorkerKey.ShouldBe("codex");
        dispatch.SettledAtUtc.ShouldBeNull();
    }

    [Fact]
    public void Dado_AttemptZero_Quando_CriaDispatch_Entao_Recusa()
    {
        Should.Throw<DomainException>(() =>
            Dispatch.Create("task-1", "run-1", "codex", attempt: 0, preambleJson: "{}"));
    }

    [Fact]
    public void Dado_Dispatch_Quando_CicloDeVida_Entao_Estados()
    {
        var dispatch = Dispatch.Create("task-1", "run-1", "codex", 1, "{}");

        dispatch.MarkDispatched();
        dispatch.State.ShouldBe(DispatchState.Dispatched);
        dispatch.DispatchedAtUtc.ShouldNotBeNull();

        dispatch.Settle();
        dispatch.State.ShouldBe(DispatchState.Settled);
        dispatch.SettledAtUtc.ShouldNotBeNull();

        dispatch.Retain();
        dispatch.State.ShouldBe(DispatchState.Retained);

        dispatch.Release();
        dispatch.State.ShouldBe(DispatchState.Released);
    }

    [Fact]
    public void Dado_DispatchPendente_Quando_Abandona_Entao_Abandoned()
    {
        var dispatch = Dispatch.Create("task-1", "run-1", "codex", 1, "{}");
        dispatch.Abandon();
        dispatch.State.ShouldBe(DispatchState.Abandoned);
    }

    [Fact]
    public void Dado_DispatchReleased_Quando_Settle_Entao_Recusa()
    {
        var dispatch = Dispatch.Create("task-1", "run-1", "codex", 1, "{}");
        dispatch.MarkDispatched();
        dispatch.Settle();
        dispatch.Release();

        Should.Throw<DomainException>(() => dispatch.Settle());
    }

    // ---- Delivery (RF-003) ----

    [Fact]
    public void Dado_DeliveryValida_Quando_Cria_Entao_NaoAcked()
    {
        var delivery = Delivery.Create("run-1", ["msg-1", "msg-2"]);

        delivery.Id.ShouldStartWith("del-");
        delivery.RunId.ShouldBe("run-1");
        delivery.MessageIds.ShouldBe(["msg-1", "msg-2"]);
        delivery.AckedAtUtc.ShouldBeNull();
    }

    [Fact]
    public void Dado_Delivery_Quando_AckDuasVezes_Entao_MantemPrimeiraData()
    {
        var delivery = Delivery.Create("run-1", ["msg-1"]);
        delivery.Ack(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        delivery.Ack(new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc));

        delivery.AckedAtUtc.ShouldBe(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Dado_SemMensagens_Quando_CriaDelivery_Entao_Recusa()
    {
        Should.Throw<DomainException>(() => Delivery.Create("run-1", []));
    }

    [Fact]
    public void Dado_MaisDeCinquenta_Quando_CriaDelivery_Entao_Recusa()
    {
        var ids = Enumerable.Range(0, 51).Select(i => $"msg-{i}").ToList();
        var ex = Should.Throw<DomainException>(() => Delivery.Create("run-1", ids));
        ex.Message.ShouldContain("50");
    }

    // ---- DelegationTask — campos v2 (RF-002/RF-007) ----

    [Fact]
    public void Dado_Task_Quando_AttachDispatch_Entao_ActiveDispatch()
    {
        var task = DelegationTask.Create("p", "codex", "conv-1", "/ws", runId: "run-1");
        task.RunId.ShouldBe("run-1");
        task.ActiveDispatchId.ShouldBeNull();

        task.AttachDispatch("disp-1");
        task.ActiveDispatchId.ShouldBe("disp-1");

        task.ClearDispatch();
        task.ActiveDispatchId.ShouldBeNull();
    }

    [Fact]
    public void Dado_TaskFalha_Quando_ReactivateForRetry_Entao_VoltaPending()
    {
        var task = DelegationTask.Create("p", "codex", "conv-1", "/ws");
        task.MarkReady();
        task.MarkRunning();
        task.MarkFailed("boom");

        task.ReactivateForRetry();

        task.Status.ShouldBe(DelegationTaskStatus.Pending);
        task.Error.ShouldBeNull();
        task.FinishedAt.ShouldBeNull();
        task.IsTerminal.ShouldBeFalse();
    }

    [Fact]
    public void Dado_TaskPronta_Quando_ReactivateForRetry_Entao_Recusa()
    {
        var task = DelegationTask.Create("p", "codex", "conv-1", "/ws");
        Should.Throw<DomainException>(() => task.ReactivateForRetry());
    }

    // ---- AgentMailboxMessage — campos v2 (RF-002/RF-003) ----

    [Fact]
    public void Dado_Mensagem_Quando_AssignDelivery_Entao_DeliveryId()
    {
        var msg = AgentMailboxMessage.Create("conv-1", "codex", "@all", "done",
            kind: AgentMailboxKinds.WorkerDone, dispatchId: "disp-1");

        msg.DispatchId.ShouldBe("disp-1");
        msg.DeliveryId.ShouldBeNull();

        msg.AssignDelivery("del-1");
        msg.DeliveryId.ShouldBe("del-1");
    }
}
