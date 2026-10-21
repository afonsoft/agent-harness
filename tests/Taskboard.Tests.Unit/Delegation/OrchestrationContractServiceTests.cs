using NSubstitute;
using Shouldly;
using Taskboard;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Delegation;
using Taskboard.Delegation;
using Taskboard.Dtos;
using Xunit;

namespace Taskboard.Tests.Unit.Delegation;

/// <summary>
/// SPEC-20261020-orchestration-contract-v2 (P1): run namespace, autoridade de
/// completion por dispatchId, retry → attempt+1 e deliveries FIFO com ack.
/// </summary>
public class OrchestrationContractServiceTests
{
    private readonly IDelegationTaskRepository _tasks = Substitute.For<IDelegationTaskRepository>();
    private readonly IAgentMailboxRepository _mailbox = Substitute.For<IAgentMailboxRepository>();
    private readonly IDelegationRunRepository _runs = Substitute.For<IDelegationRunRepository>();
    private readonly IDispatchRepository _dispatches = Substitute.For<IDispatchRepository>();
    private readonly IDeliveryRepository _deliveries = Substitute.For<IDeliveryRepository>();
    private readonly IPendingQuestionRepository _questions = Substitute.For<IPendingQuestionRepository>();
    private readonly IDelegationGateRepository _gates = Substitute.For<IDelegationGateRepository>();
    private readonly DelegationService _service;

    public OrchestrationContractServiceTests()
    {
        _service = new DelegationService(
            _tasks, _mailbox, _runs, _dispatches, _deliveries,
            _questions, _gates, TimeProvider.System);
    }

    private static DelegationTaskDto TaskDto(
        string id = "task-1", string scope = "conv-1",
        DelegationTaskStatus status = DelegationTaskStatus.Pending,
        string? runId = "run-1", string? activeDispatch = null) =>
        new(id, scope, "prompt", "codex", [], null, null, false, null,
            "/ws", null, null, status, null, null,
            DateTime.UtcNow, null, null, null, "task", runId, activeDispatch);

    private static DelegationRunDto RunDto(
        string id = "run-1", string scope = "conv-1",
        DelegationRunStatus status = DelegationRunStatus.Open) =>
        new(id, scope, "obj", "assistant", status, DateTime.UtcNow, null);

    private static DispatchDto DispatchDtoOf(
        string id = "disp-1", string taskId = "task-1", int attempt = 1,
        DispatchState state = DispatchState.Dispatched) =>
        new(id, taskId, "run-1", "codex", attempt, state, "{}",
            null, null, DateTime.UtcNow, DateTime.UtcNow, null, null);

    private static MailboxMessageDto MsgDto(
        string id, string scope = "conv-1", string toAgent = "@all",
        string kind = "text", string? deliveryId = null) =>
        new(id, scope, "codex", toAgent, kind, $"payload-{id}",
            DateTime.UtcNow, null, deliveryId, null);

    // ---- RF-001: run namespace ----

    [Fact]
    public async Task Dado_SemRunAberto_Quando_CriaTask_Entao_CriaRunEAtribui()
    {
        _runs.GetOpenByScopeAsync("conv-1", Arg.Any<CancellationToken>()).Returns((DelegationRunDto?)null);
        _runs.AddAsync("conv-1", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RunDto("run-new"));
        _tasks.AddAsync(Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => TaskDto("task-9", runId: ((CreateDelegationTaskRequest)ci[0]).RunId));

        var task = await _service.CreateTaskAsync(
            new CreateDelegationTaskRequest("p", "codex", "conv-1", "/ws"));

        task.RunId.ShouldBe("run-new");
        await _runs.Received(1).AddAsync(
            "conv-1", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_RunAberto_Quando_CriaTask_Entao_ReusaRun()
    {
        _runs.GetOpenByScopeAsync("conv-1", Arg.Any<CancellationToken>()).Returns(RunDto());
        _tasks.AddAsync(Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => TaskDto("task-9", runId: ((CreateDelegationTaskRequest)ci[0]).RunId));

        var task = await _service.CreateTaskAsync(
            new CreateDelegationTaskRequest("p", "codex", "conv-1", "/ws"));

        task.RunId.ShouldBe("run-1");
        await _runs.DidNotReceive().AddAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_Run_Quando_Show_Entao_ExpoeTasksDispatchesDeliveries()
    {
        _runs.GetAsync("run-1", Arg.Any<CancellationToken>()).Returns(RunDto());
        _tasks.ListByScopeAsync("conv-1", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([TaskDto("task-1"), TaskDto("task-2", runId: "run-legacy")]);
        _dispatches.ListOpenByRunAsync("run-1", Arg.Any<CancellationToken>())
            .Returns([DispatchDtoOf("disp-1")]);
        _deliveries.ListByRunAsync("run-1", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                new DeliveryDto("del-1", "run-1", ["m1"], DateTime.UtcNow, null),
                new DeliveryDto("del-2", "run-1", ["m2"], DateTime.UtcNow, DateTime.UtcNow),
            ]);

        var show = await _service.GetRunShowAsync("run-1", "conv-1");

        show.ShouldNotBeNull();
        show.Run.Id.ShouldBe("run-1");
        show.Tasks.Select(t => t.Id).ShouldBe(["task-1"]);
        show.OpenDispatches.Select(d => d.Id).ShouldBe(["disp-1"]);
        show.UnackedDeliveries.Select(d => d.Id).ShouldBe(["del-1"]);
    }

    // ---- RF-002: dispatch authority ----

    [Fact]
    public async Task Dado_TaskReady_Quando_BeginRun_Entao_CriaDispatchAttempt1()
    {
        var ready = TaskDto("task-1", status: DelegationTaskStatus.Ready);
        _tasks.MarkRunningAsync("task-1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(ready);
        _tasks.AttachDispatchAsync("task-1", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci => ready);
        _dispatches.ListByTaskAsync("task-1", Arg.Any<CancellationToken>()).Returns([]);
        _dispatches.LatestAttemptAsync("task-1", Arg.Any<CancellationToken>()).Returns(0);
        _dispatches.AddAsync(Arg.Any<CreateDispatchRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var r = (CreateDispatchRequest)ci[0];
                return DispatchDtoOf("disp-new", r.TaskId, r.Attempt, DispatchState.Pending);
            });
        _dispatches.TransitionAsync(
                "disp-new", DispatchState.Dispatched, null, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(DispatchDtoOf("disp-new"));

        var task = await _service.BeginRunAsync("task-1", DateTime.UtcNow);

        task.ShouldNotBeNull();
        await _dispatches.Received(1).AddAsync(
            Arg.Is<CreateDispatchRequest>(r =>
                r.TaskId == "task-1" && r.RunId == "run-1" && r.Attempt == 1
                && r.PreambleJson.Contains("task-1")),
            Arg.Any<CancellationToken>());
        await _tasks.Received(1).AttachDispatchAsync(
            "task-1", "disp-new", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_RetryComDispatchPendente_Quando_BeginRun_Entao_DispachaExistente()
    {
        var ready = TaskDto("task-1", status: DelegationTaskStatus.Ready);
        _tasks.MarkRunningAsync("task-1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(ready);
        _tasks.AttachDispatchAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ready);
        _dispatches.ListByTaskAsync("task-1", Arg.Any<CancellationToken>())
            .Returns([DispatchDtoOf("disp-9", attempt: 2, state: DispatchState.Pending)]);
        _dispatches.TransitionAsync(
                "disp-9", DispatchState.Dispatched, null, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(DispatchDtoOf("disp-9", attempt: 2));

        await _service.BeginRunAsync("task-1", DateTime.UtcNow);

        await _dispatches.DidNotReceive().AddAsync(
            Arg.Any<CreateDispatchRequest>(), Arg.Any<CancellationToken>());
        await _dispatches.Received(1).TransitionAsync(
            "disp-9", DispatchState.Dispatched, null, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_DispatchAtivo_Quando_FinishRun_Entao_SettleEPostaWorkerDoneComDispatch()
    {
        var finished = TaskDto("task-1", status: DelegationTaskStatus.Done, activeDispatch: "disp-1");
        _tasks.FinalizeAsync(
                "task-1", DelegationTaskStatus.Done, Arg.Any<string?>(),
                Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(finished);
        _dispatches.TransitionAsync(
                "disp-1", DispatchState.Settled, null, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(DispatchDtoOf("disp-1", state: DispatchState.Settled));
        _mailbox.AddAsync(Arg.Any<PostMailboxMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var r = (PostMailboxMessageRequest)ci[0];
                return MsgDto("msg-1", kind: r.Kind, deliveryId: null) with { DispatchId = r.DispatchId };
            });

        await _service.FinishRunAsync("task-1", succeeded: true, "ok");

        await _dispatches.Received(1).TransitionAsync(
            "disp-1", DispatchState.Settled, null, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _mailbox.Received(1).AddAsync(
            Arg.Is<PostMailboxMessageRequest>(r =>
                r.Kind == AgentMailboxKinds.WorkerDone
                && r.TaskId == "task-1" && r.DispatchId == "disp-1"),
            Arg.Any<CancellationToken>());
        await _tasks.Received(1).AttachDispatchAsync(
            "task-1", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_WorkerDoneComDispatchStale_Quando_PostAsync_Entao_RecusaStaleDispatch()
    {
        _dispatches.GetAsync("disp-x", Arg.Any<CancellationToken>())
            .Returns(DispatchDtoOf("disp-x", taskId: "task-1"));
        _tasks.GetAsync("task-1", Arg.Any<CancellationToken>())
            .Returns(TaskDto("task-1", activeDispatch: "disp-other"));

        var ex = await Should.ThrowAsync<DomainException>(() =>
            _service.PostAsync(new PostMailboxMessageRequest(
                "conv-1", "codex", "@all", "done",
                AgentMailboxKinds.WorkerDone, "task-1", "disp-x")));

        ex.Message.ShouldContain("stale_dispatch");
        await _mailbox.DidNotReceive().AddAsync(
            Arg.Any<PostMailboxMessageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_WorkerDoneLegadoSemDispatch_Quando_PostAsync_Entao_AttachaAtivo()
    {
        _tasks.GetAsync("task-1", Arg.Any<CancellationToken>())
            .Returns(TaskDto("task-1", activeDispatch: "disp-active"));
        _mailbox.AddAsync(Arg.Any<PostMailboxMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var r = (PostMailboxMessageRequest)ci[0];
                return MsgDto("msg-1", kind: r.Kind) with { DispatchId = r.DispatchId };
            });

        var msg = await _service.PostAsync(new PostMailboxMessageRequest(
            "conv-1", "codex", "@all", "done", AgentMailboxKinds.WorkerDone, TaskId: "task-1"));

        msg.DispatchId.ShouldBe("disp-active");
    }

    // ---- RF-007: retry → attempt+1 no mesmo task ----

    [Fact]
    public async Task Dado_TaskFalhaComDispatch_Quando_RetryPorDispatchId_Entao_AttemptMaisUmMesmoTask()
    {
        _dispatches.GetAsync("disp-1", Arg.Any<CancellationToken>())
            .Returns(DispatchDtoOf("disp-1", "task-1", attempt: 1));
        _tasks.ListByIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), "conv-1", Arg.Any<CancellationToken>())
            .Returns([TaskDto("task-1", status: DelegationTaskStatus.Failed)]);
        _dispatches.LatestAttemptAsync("task-1", Arg.Any<CancellationToken>()).Returns(1);
        _dispatches.AddAsync(Arg.Any<CreateDispatchRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var r = (CreateDispatchRequest)ci[0];
                return DispatchDtoOf("disp-2", r.TaskId, r.Attempt, DispatchState.Pending);
            });
        _tasks.ReactivateForRetryAsync("task-1", Arg.Any<CancellationToken>())
            .Returns(TaskDto("task-1", status: DelegationTaskStatus.Pending, runId: "run-1"));

        var result = await _service.RetryTaskAsync("conv-1", "disp-1");

        result.Task.Id.ShouldBe("task-1");
        result.Task.Status.ShouldBe(DelegationTaskStatus.Pending);
        result.Dispatch.Attempt.ShouldBe(2);
        result.Dispatch.TaskId.ShouldBe("task-1");
        await _tasks.DidNotReceive().AddAsync(
            Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_TaskFalha_Quando_RetryPorTaskId_Entao_AttemptMaisUm()
    {
        _tasks.ListByIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), "conv-1", Arg.Any<CancellationToken>())
            .Returns([TaskDto("task-1", status: DelegationTaskStatus.Failed)]);
        _dispatches.LatestAttemptAsync("task-1", Arg.Any<CancellationToken>()).Returns(2);
        _dispatches.AddAsync(Arg.Any<CreateDispatchRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var r = (CreateDispatchRequest)ci[0];
                return DispatchDtoOf("disp-3", r.TaskId, r.Attempt, DispatchState.Pending);
            });
        _tasks.ReactivateForRetryAsync("task-1", Arg.Any<CancellationToken>())
            .Returns(TaskDto("task-1", status: DelegationTaskStatus.Pending, runId: "run-1"));

        var result = await _service.RetryTaskAsync("conv-1", "task-1");

        result.Dispatch.Attempt.ShouldBe(3);
    }

    [Fact]
    public async Task Dado_TaskRodando_Quando_Retry_Entao_Recusa()
    {
        _tasks.ListByIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), "conv-1", Arg.Any<CancellationToken>())
            .Returns([TaskDto("task-1", status: DelegationTaskStatus.Running)]);

        await Should.ThrowAsync<DomainException>(() =>
            _service.RetryTaskAsync("conv-1", "task-1"));
    }

    [Fact]
    public async Task Dado_RetryDesconhecido_Quando_Retry_Entao_Recusa()
    {
        _tasks.ListByIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), "conv-1", Arg.Any<CancellationToken>()).Returns([]);
        _dispatches.GetAsync("disp-x", Arg.Any<CancellationToken>()).Returns((DispatchDto?)null);

        await Should.ThrowAsync<DomainException>(() =>
            _service.RetryTaskAsync("conv-1", "disp-x"));
    }

    // ---- RF-003: deliveries FIFO + ack ----

    private void StubRun() =>
        _runs.GetOpenByScopeAsync("conv-1", Arg.Any<CancellationToken>()).Returns(RunDto());

    [Fact]
    public async Task Dado_SemMensagens_Quando_Check_Entao_DeliveryVazio()
    {
        StubRun();
        _deliveries.GetOldestUnackedAsync("run-1", Arg.Any<CancellationToken>())
            .Returns((DeliveryDto?)null);
        _mailbox.ListUndeliveredForRecipientAsync(
                "conv-1", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<int>(),
                Arg.Any<IReadOnlyCollection<string>?>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await _service.CheckDeliveriesAsync(
            new CheckDeliveriesRequest("conv-1", ["@all"]));

        result.Delivery.ShouldBeNull();
        result.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_MensagensPendentes_Quando_Check_Entao_CriaDeliveryERetornaEmOrdem()
    {
        StubRun();
        _deliveries.GetOldestUnackedAsync("run-1", Arg.Any<CancellationToken>())
            .Returns((DeliveryDto?)null);
        _mailbox.ListUndeliveredForRecipientAsync(
                "conv-1", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<int>(),
                Arg.Any<IReadOnlyCollection<string>?>(), Arg.Any<CancellationToken>())
            .Returns([MsgDto("m1"), MsgDto("m2"), MsgDto("m3")]);
        _deliveries.AddAsync("run-1", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => new DeliveryDto(
                "del-1", "run-1", (IReadOnlyList<string>)ci[1], DateTime.UtcNow, null));

        var result = await _service.CheckDeliveriesAsync(
            new CheckDeliveriesRequest("conv-1", ["@all"]));

        result.Delivery.ShouldNotBeNull();
        result.Delivery.Id.ShouldBe("del-1");
        result.Delivery.MessageIds.ShouldBe(["m1", "m2", "m3"]);
        result.Messages.Select(m => m.Id).ShouldBe(["m1", "m2", "m3"]);
        await _mailbox.Received(1).AssignDeliveryAsync(
            Arg.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(new[] { "m1", "m2", "m3" })),
            "del-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_DeliveryNaoAcked_Quando_Check_Entao_ReplaySemAck()
    {
        StubRun();
        _deliveries.GetOldestUnackedAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(new DeliveryDto("del-1", "run-1", ["m1", "m2"], DateTime.UtcNow, null));
        _mailbox.ListByIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ((IReadOnlyCollection<string>)ci[0]).Select(id => MsgDto(id)).ToList());

        var result = await _service.CheckDeliveriesAsync(
            new CheckDeliveriesRequest("conv-1", ["@all"]));

        result.Delivery!.Id.ShouldBe("del-1");
        result.Messages.Select(m => m.Id).ShouldBe(["m1", "m2"]);
        await _deliveries.DidNotReceive().AddAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_AckDeDelivery_Quando_Check_Entao_MarcaERetornaProxima()
    {
        StubRun();
        _deliveries.AckAsync("del-1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new DeliveryDto("del-1", "run-1", ["m1"], DateTime.UtcNow, DateTime.UtcNow));
        _deliveries.GetOldestUnackedAsync("run-1", Arg.Any<CancellationToken>())
            .Returns(new DeliveryDto("del-2", "run-1", ["m2"], DateTime.UtcNow, null));
        _mailbox.ListByIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ((IReadOnlyCollection<string>)ci[0]).Select(id => MsgDto(id)).ToList());

        var result = await _service.CheckDeliveriesAsync(
            new CheckDeliveriesRequest("conv-1", ["@all"], AckDeliveryId: "del-1"));

        result.Delivery!.Id.ShouldBe("del-2");
        await _deliveries.Received(1).AckAsync(
            "del-1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_AckInexistente_Quando_Check_Entao_DeliveryNotFound()
    {
        StubRun();
        _deliveries.AckAsync("del-x", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((DeliveryDto?)null);

        var ex = await Should.ThrowAsync<DomainException>(() =>
            _service.CheckDeliveriesAsync(
                new CheckDeliveriesRequest("conv-1", ["@all"], AckDeliveryId: "del-x")));
        ex.Message.ShouldContain("delivery_not_found");
    }

    [Fact]
    public async Task Dado_Peek_Quando_Check_Entao_NaoConsome()
    {
        StubRun();
        _mailbox.ListUndeliveredForRecipientAsync(
                "conv-1", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<int>(),
                Arg.Any<IReadOnlyCollection<string>?>(), Arg.Any<CancellationToken>())
            .Returns([MsgDto("m1")]);

        var result = await _service.CheckDeliveriesAsync(
            new CheckDeliveriesRequest("conv-1", ["@all"], Peek: true));

        result.Delivery.ShouldBeNull();
        result.Messages.Select(m => m.Id).ShouldBe(["m1"]);
        result.PendingCount.ShouldBe(1);
        await _deliveries.DidNotReceive().AddAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_All_Quando_Check_Entao_ListaDeliveries()
    {
        StubRun();
        _deliveries.ListByRunAsync("run-1", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                new DeliveryDto("del-1", "run-1", ["m1"], DateTime.UtcNow, DateTime.UtcNow),
                new DeliveryDto("del-2", "run-1", ["m2"], DateTime.UtcNow, null),
            ]);

        var result = await _service.CheckDeliveriesAsync(
            new CheckDeliveriesRequest("conv-1", ["@all"], All: true));

        result.Deliveries.ShouldNotBeNull();
        result.Deliveries!.Select(d => d.Id).ShouldBe(["del-1", "del-2"]);
    }

    [Fact]
    public async Task Dado_FiltroPorTipo_Quando_Check_Entao_SoDoTipo()
    {
        StubRun();
        _deliveries.GetOldestUnackedAsync("run-1", Arg.Any<CancellationToken>())
            .Returns((DeliveryDto?)null);
        _mailbox.ListUndeliveredForRecipientAsync(
                "conv-1", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<int>(),
                Arg.Any<IReadOnlyCollection<string>?>(), Arg.Any<CancellationToken>())
            .Returns([MsgDto("m1", kind: "heartbeat"), MsgDto("m2", kind: "worker_done")]);
        _deliveries.AddAsync("run-1", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => new DeliveryDto(
                "del-1", "run-1", (IReadOnlyList<string>)ci[1], DateTime.UtcNow, null));

        var result = await _service.CheckDeliveriesAsync(
            new CheckDeliveriesRequest("conv-1", ["@all"], Types: ["worker_done"]));

        result.Delivery!.MessageIds.ShouldBe(["m2"]);
    }

    [Fact]
    public async Task Dado_SessentaPendentes_Quando_Check_Entao_BatchDeCinquenta()
    {
        StubRun();
        _deliveries.GetOldestUnackedAsync("run-1", Arg.Any<CancellationToken>())
            .Returns((DeliveryDto?)null);
        var pending = Enumerable.Range(1, 60).Select(i => MsgDto($"m{i}")).ToList();
        _mailbox.ListUndeliveredForRecipientAsync(
                "conv-1", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<int>(),
                Arg.Any<IReadOnlyCollection<string>?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
                pending.Take((int)ci[2]).ToList());
        _deliveries.AddAsync("run-1", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => new DeliveryDto(
                "del-1", "run-1", (IReadOnlyList<string>)ci[1], DateTime.UtcNow, null));

        var result = await _service.CheckDeliveriesAsync(
            new CheckDeliveriesRequest("conv-1", ["@all"]));

        result.Delivery!.MessageIds.Count.ShouldBe(50);
        result.PendingCount.ShouldBe(51);
        await _mailbox.Received(1).ListUndeliveredForRecipientAsync(
            "conv-1", Arg.Any<IReadOnlyCollection<string>>(),
            Arg.Is<int>(take => take == 51), Arg.Any<IReadOnlyCollection<string>?>(),
            Arg.Any<CancellationToken>());
    }
}
