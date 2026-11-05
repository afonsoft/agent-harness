using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Delegation;
using Taskboard.Domain.Entities.Delegation;
using Taskboard.Dtos;
using Xunit;
using static Taskboard.Tests.Unit.Delegation.DelegationServiceStubs;

namespace Taskboard.Tests.Unit.Delegation;

/// <summary>SPEC-20261104-agent-lineage-notes: lineage, cascade, notes, outbox.</summary>
public class AgentLineageNotesTests
{
    [Fact]
    public void Dado_EntryValida_Quando_Enqueue_Entao_CamposPreenchidos()
    {
        var entry = AgentOutboxEntry.Enqueue("disp:p1", "msg-1", "note");
        entry.SessionKey.ShouldBe("disp:p1");
        entry.MessageId.ShouldBe("msg-1");
        entry.Kind.ShouldBe("note");
        entry.Id.ShouldStartWith("ob-");
    }

    [Fact]
    public void Dado_SemSession_Quando_Enqueue_Entao_Excecao()
    {
        Should.Throw<DomainException>(() => AgentOutboxEntry.Enqueue("  ", "msg-1", "note"));
    }

    [Fact]
    public void Dado_DispatchComPai_Quando_Create_Entao_ParentDispatchIdPersistido()
    {
        var child = Dispatch.Create("t1", "r1", "w", 1, "{}", parentDispatchId: "disp-parent");
        child.ParentDispatchId.ShouldBe("disp-parent");
        Dispatch.Create("t1", "r1", "w", 1, "{}").ParentDispatchId.ShouldBeNull();
    }

    [Fact]
    public void Dado_Payload_Quando_Wrap_Entao_EnvelopeDeProveniencia()
    {
        var wrapped = MailboxEnvelopeFormatter.Wrap("codex", "t-1", "conv-1", "hello", null);

        wrapped.ShouldStartWith(
            "<harness from=\"codex t-1\" scope=\"conv-1\" reach=\"taskctl agent:send \\\"<reply>\\\"\">");
        wrapped.ShouldContain("\nhello\n");
        wrapped.ShouldEndWith("</harness>");
    }

    [Fact]
    public void Dado_PayloadComSegredo_Quando_Wrap_Entao_RedaAntesDeWrap()
    {
        var redactor = Substitute.For<ISecretRedactor>();
        redactor.Redact("token=abc").Returns("token=***");

        var wrapped = MailboxEnvelopeFormatter.Wrap("codex", "t", "s", "token=abc", redactor);

        wrapped.ShouldContain("token=***");
        wrapped.ShouldNotContain("token=abc");
    }

    [Fact]
    public void Dado_AspasNoFrom_Quando_Wrap_Entao_Escapadas()
    {
        var wrapped = MailboxEnvelopeFormatter.Wrap("co\"dex", "t\"1", "s", "x", null);

        wrapped.ShouldContain("from=\"co'dex t'1\"");
    }

    [Fact]
    public async Task Dado_PaiDesconhecido_Quando_CreateTask_Entao_Recusa()
    {
        var f = new DelegationServiceStubs();
        f.Runs.GetOpenByScopeAsync("conv-1", Arg.Any<CancellationToken>()).Returns(RunDto());
        f.Dispatches.GetAsync("disp-x", Arg.Any<CancellationToken>()).Returns((DispatchDto?)null);
        f.Tasks.GetAsync("disp-x", Arg.Any<CancellationToken>()).Returns((DelegationTaskDto?)null);

        await Should.ThrowAsync<DomainException>(() => f.Service.CreateTaskAsync(
            new CreateDelegationTaskRequest(
                "p", "codex", "conv-1", "/ws", ParentDispatchId: "disp-x")));
    }

    [Fact]
    public async Task Dado_FilhoComPai_Quando_PaiFinaliza_Entao_CascadeCancela()
    {
        var f = new DelegationServiceStubs();
        var parentTask = TaskDto("t-parent") with { ActiveDispatchId = "disp-parent" };
        var childTask = TaskDto("t-child") with { OutliveParent = false };
        var childDispatch = DispatchDto("disp-child",
            state: DispatchState.Dispatched, taskId: "t-child");

        f.Tasks.FinalizeAsync("t-parent", DelegationTaskStatus.Done, Arg.Any<string?>(),
                Arg.Any<DateTime>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(parentTask with { Status = DelegationTaskStatus.Done });
        f.Dispatches.ListByParentAsync("disp-parent", Arg.Any<CancellationToken>())
            .Returns([childDispatch]);
        f.Tasks.GetAsync("t-child", Arg.Any<CancellationToken>()).Returns(childTask);
        f.Tasks.FinalizeAsync("t-child", DelegationTaskStatus.Cancelled, Arg.Any<string?>(),
                Arg.Any<DateTime>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(childTask with { Status = DelegationTaskStatus.Cancelled });
        f.Mailbox.AddAsync(Arg.Any<PostMailboxMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MailboxMessageDto(
                "m1", "conv-1", "codex", "@all", "x", "worker_done", DateTime.UtcNow, null));

        await f.Service.FinishRunAsync("t-parent", succeeded: true, "ok");

        await f.Tasks.Received(1).FinalizeAsync(
            "t-child", DelegationTaskStatus.Cancelled, "parent_finished",
            Arg.Any<DateTime>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>());
        await f.Dispatches.Received(1).TransitionAsync(
            "disp-child", DispatchState.Abandoned, null,
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_FilhoOutlive_Quando_PaiFinaliza_Entao_SemCascade()
    {
        var f = new DelegationServiceStubs();
        var parentTask = TaskDto("t-parent") with { ActiveDispatchId = "disp-parent" };
        var childTask = TaskDto("t-child") with { OutliveParent = true };
        var childDispatch = DispatchDto("disp-child",
            state: DispatchState.Dispatched, taskId: "t-child");

        f.Tasks.FinalizeAsync("t-parent", DelegationTaskStatus.Done, Arg.Any<string?>(),
                Arg.Any<DateTime>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(parentTask with { Status = DelegationTaskStatus.Done });
        f.Dispatches.ListByParentAsync("disp-parent", Arg.Any<CancellationToken>())
            .Returns([childDispatch]);
        f.Tasks.GetAsync("t-child", Arg.Any<CancellationToken>()).Returns(childTask);
        f.Mailbox.AddAsync(Arg.Any<PostMailboxMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MailboxMessageDto(
                "m1", "conv-1", "codex", "@all", "x", "worker_done", DateTime.UtcNow, null));

        await f.Service.FinishRunAsync("t-parent", succeeded: true, "ok");

        await f.Tasks.DidNotReceive().FinalizeAsync(
            "t-child", Arg.Any<DelegationTaskStatus>(), Arg.Any<string?>(),
            Arg.Any<DateTime>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_FilhoComPaiENotify_Quando_FilhoFinaliza_Entao_NotificaPai()
    {
        var f = new DelegationServiceStubs();
        var childTask = TaskDto("t-child") with
        {
            ActiveDispatchId = "disp-child",
            ParentDispatchId = "disp-parent",
            NotifyParent = true,
        };
        f.Tasks.FinalizeAsync("t-child", DelegationTaskStatus.Done, Arg.Any<string?>(),
                Arg.Any<DateTime>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(childTask with { Status = DelegationTaskStatus.Done });
        f.Dispatches.GetAsync("disp-parent", Arg.Any<CancellationToken>())
            .Returns(DispatchDto("disp-parent", DispatchState.Dispatched, "t-parent"));
        f.Dispatches.ListByParentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);
        f.Mailbox.AddAsync(Arg.Any<PostMailboxMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MailboxMessageDto(
                "m1", "conv-1", "codex", "x", "x", "note", DateTime.UtcNow, null));

        await f.Service.FinishRunAsync("t-child", succeeded: true, "ok");

        await f.Mailbox.Received(1).AddAsync(
            Arg.Is<PostMailboxMessageRequest>(r =>
                r.Kind == AgentMailboxKinds.Note
                && r.ToAgent == "disp:disp-parent"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_PaiMorto_Quando_FilhoFinaliza_Entao_NotaVaiParaAll()
    {
        var f = new DelegationServiceStubs();
        var childTask = TaskDto("t-child") with
        {
            ActiveDispatchId = "disp-child",
            ParentDispatchId = "disp-parent",
            NotifyParent = true,
        };
        f.Tasks.FinalizeAsync("t-child", DelegationTaskStatus.Done, Arg.Any<string?>(),
                Arg.Any<DateTime>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(childTask with { Status = DelegationTaskStatus.Done });
        f.Dispatches.GetAsync("disp-parent", Arg.Any<CancellationToken>())
            .Returns(DispatchDto("disp-parent", DispatchState.Settled, "t-parent"));
        f.Dispatches.ListByParentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);
        f.Mailbox.AddAsync(Arg.Any<PostMailboxMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MailboxMessageDto(
                "m1", "conv-1", "codex", "x", "x", "note", DateTime.UtcNow, null));

        await f.Service.FinishRunAsync("t-child", succeeded: true, "ok");

        await f.Mailbox.Received(1).AddAsync(
            Arg.Is<PostMailboxMessageRequest>(r =>
                r.Kind == AgentMailboxKinds.Note && r.ToAgent == "@all"),
            Arg.Any<CancellationToken>());
        // No live parent → nothing pushed, but the note still queues for the
        // dispatch inbox key (index only — mailbox is the durable path).
        await f.Outbox.Received(1).EnqueueAsync(
            "disp:disp-parent", "m1", AgentMailboxKinds.Note,
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_TerminalPaiVivo_Quando_Nota_Entao_EnvelopeInjetado()
    {
        var f = new DelegationServiceStubs();
        var childTask = TaskDto("t-child") with
        {
            ActiveDispatchId = "disp-child",
            ParentDispatchId = "disp-parent",
            NotifyParent = true,
        };
        var parentTerminal = new RunTerminalDto(
            "rt-1", "t-parent", "disp-parent", "run-t-parent", "codex",
            RunTerminalState.Live, RunTerminalControlOwner.Operator,
            "working", DateTime.UtcNow, null, null, true, null);
        f.Tasks.FinalizeAsync("t-child", DelegationTaskStatus.Done, Arg.Any<string?>(),
                Arg.Any<DateTime>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(childTask with { Status = DelegationTaskStatus.Done });
        f.Dispatches.GetAsync("disp-parent", Arg.Any<CancellationToken>())
            .Returns(DispatchDto("disp-parent", DispatchState.Dispatched, "t-parent"));
        f.Dispatches.ListByParentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);
        f.Mailbox.AddAsync(Arg.Any<PostMailboxMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MailboxMessageDto(
                "m1", "conv-1", "codex", "x", "x", "note", DateTime.UtcNow, null));
        f.RunTerminals.GetAsync("disp-parent", "conv-1", Arg.Any<CancellationToken>())
            .Returns(parentTerminal);
        f.RunTerminals.SendAsync(
                "rt-1", Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new RunTerminalOpResult(true, parentTerminal));

        await f.Service.FinishRunAsync("t-child", succeeded: true, "ok");

        await f.RunTerminals.Received(1).SendAsync(
            "rt-1",
            Arg.Is<string>(s => s.Contains("<harness") && s.Contains("childTaskId")),
            Arg.Any<bool>(), true, "conv-1", Arg.Any<CancellationToken>());
        await f.Outbox.DidNotReceive().EnqueueAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_InjectRecusado_Quando_Nota_Entao_OutboxTermKey()
    {
        var f = new DelegationServiceStubs();
        var childTask = TaskDto("t-child") with
        {
            ActiveDispatchId = "disp-child",
            ParentDispatchId = "disp-parent",
            NotifyParent = true,
        };
        var parentTerminal = new RunTerminalDto(
            "rt-1", "t-parent", "disp-parent", "run-t-parent", "codex",
            RunTerminalState.Live, RunTerminalControlOwner.Human,
            "working", DateTime.UtcNow, null, null, true, null);
        f.Tasks.FinalizeAsync("t-child", DelegationTaskStatus.Done, Arg.Any<string?>(),
                Arg.Any<DateTime>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(childTask with { Status = DelegationTaskStatus.Done });
        f.Dispatches.GetAsync("disp-parent", Arg.Any<CancellationToken>())
            .Returns(DispatchDto("disp-parent", DispatchState.Dispatched, "t-parent"));
        f.Dispatches.ListByParentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);
        f.Mailbox.AddAsync(Arg.Any<PostMailboxMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MailboxMessageDto(
                "m1", "conv-1", "codex", "x", "x", "note", DateTime.UtcNow, null));
        f.RunTerminals.GetAsync("disp-parent", "conv-1", Arg.Any<CancellationToken>())
            .Returns(parentTerminal);
        f.RunTerminals.SendAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new RunTerminalOpResult(
                false, parentTerminal,
                new RunTerminalError(RunTerminalErrorKinds.StatusMismatch, "human driving")));

        await f.Service.FinishRunAsync("t-child", succeeded: true, "ok");

        await f.Outbox.Received(1).EnqueueAsync(
            "term:run-t-parent", "m1", AgentMailboxKinds.Note,
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_Overflow_Quando_Enqueue_Entao_FeedOutboxOverflow()
    {
        var f = new DelegationServiceStubs();
        var childTask = TaskDto("t-child") with
        {
            ActiveDispatchId = "disp-child",
            ParentDispatchId = "disp-parent",
            NotifyParent = true,
        };
        f.Tasks.FinalizeAsync("t-child", DelegationTaskStatus.Done, Arg.Any<string?>(),
                Arg.Any<DateTime>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(childTask with { Status = DelegationTaskStatus.Done });
        f.Dispatches.GetAsync("disp-parent", Arg.Any<CancellationToken>())
            .Returns(DispatchDto("disp-parent", DispatchState.Settled, "t-parent"));
        f.Dispatches.ListByParentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);
        f.Mailbox.AddAsync(Arg.Any<PostMailboxMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MailboxMessageDto(
                "m1", "conv-1", "codex", "x", "x", "note", DateTime.UtcNow, null));
        f.Outbox.TrimToCapacityAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(3);

        await f.Service.FinishRunAsync("t-child", succeeded: true, "ok");

        await f.Feed.Received(1).ProduceAsync(
            Arg.Is<FeedItemRequest>(r => r.Kind == AgentFeedKind.OutboxOverflow),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_Cascade_Quando_PaiFinaliza_Entao_KillTerminalFilho()
    {
        var f = new DelegationServiceStubs();
        var parentTask = TaskDto("t-parent") with { ActiveDispatchId = "disp-parent" };
        var childTask = TaskDto("t-child") with { OutliveParent = false };
        var childDispatch = DispatchDto("disp-child",
            state: DispatchState.Dispatched, taskId: "t-child");

        f.Tasks.FinalizeAsync("t-parent", DelegationTaskStatus.Done, Arg.Any<string?>(),
                Arg.Any<DateTime>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(parentTask with { Status = DelegationTaskStatus.Done });
        f.Dispatches.ListByParentAsync("disp-parent", Arg.Any<CancellationToken>())
            .Returns([childDispatch]);
        f.Tasks.GetAsync("t-child", Arg.Any<CancellationToken>()).Returns(childTask);
        f.Tasks.FinalizeAsync("t-child", DelegationTaskStatus.Cancelled, Arg.Any<string?>(),
                Arg.Any<DateTime>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(childTask with { Status = DelegationTaskStatus.Cancelled });
        f.Mailbox.AddAsync(Arg.Any<PostMailboxMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MailboxMessageDto(
                "m1", "conv-1", "codex", "@all", "x", "worker_done", DateTime.UtcNow, null));

        await f.Service.FinishRunAsync("t-parent", succeeded: true, "ok");

        await f.RunTerminals.Received(1).KillAsync(
            "t-child", Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_FilhoSemNotify_Quando_FilhoFinaliza_Entao_SemNota()
    {
        var f = new DelegationServiceStubs();
        var childTask = TaskDto("t-child") with
        {
            ActiveDispatchId = "disp-child",
            ParentDispatchId = "disp-parent",
            NotifyParent = false,
        };
        f.Tasks.FinalizeAsync("t-child", DelegationTaskStatus.Done, Arg.Any<string?>(),
                Arg.Any<DateTime>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(childTask with { Status = DelegationTaskStatus.Done });
        f.Dispatches.ListByParentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);
        f.Mailbox.AddAsync(Arg.Any<PostMailboxMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MailboxMessageDto(
                "m1", "conv-1", "codex", "x", "x", "note", DateTime.UtcNow, null));

        await f.Service.FinishRunAsync("t-child", succeeded: true, "ok");

        await f.Mailbox.DidNotReceive().AddAsync(
            Arg.Is<PostMailboxMessageRequest>(r => r.Kind == AgentMailboxKinds.Note),
            Arg.Any<CancellationToken>());
    }
}
