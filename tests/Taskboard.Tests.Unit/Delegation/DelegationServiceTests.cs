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
/// SPEC-20261005-delegation-dag-mailbox: regras do serviço de delegação —
/// validação de criação, resolução do DAG, sweep de heartbeat e finalize
/// acoplado ao mailbox.
/// </summary>
public class DelegationServiceTests
{
    private readonly IDelegationTaskRepository _tasks = Substitute.For<IDelegationTaskRepository>();
    private readonly IAgentMailboxRepository _mailbox = Substitute.For<IAgentMailboxRepository>();
    private readonly DelegationService _service;

    public DelegationServiceTests()
    {
        _service = new DelegationService(_tasks, _mailbox, TimeProvider.System);
    }

    private static DelegationTaskDto TaskDto(
        string id = "task-1", string scope = "conv-1",
        DelegationTaskStatus status = DelegationTaskStatus.Pending,
        IReadOnlyList<string>? dependsOn = null) =>
        new(id, scope, "prompt", "codex", dependsOn ?? [], null, null, false, null,
            "/ws", null, null, status, null, null,
            DateTime.UtcNow, null, null, null);

    private static CreateDelegationTaskRequest Request(
        IReadOnlyList<string>? dependsOn = null, string? retryOf = null) =>
        new("do work", "codex", "conv-1", "/ws", dependsOn, retryOf);

    // ---- create validation ----

    [Fact]
    public async Task Dado_DependenciaInexistente_Quando_CriaTask_Entao_Recusa()
    {
        _tasks.ListByIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), "conv-1", Arg.Any<CancellationToken>())
            .Returns([]);

        await Should.ThrowAsync<DomainException>(() =>
            _service.CreateTaskAsync(Request(dependsOn: ["task-x"])));
    }

    [Fact]
    public async Task Dado_RetryDeTaskRodando_Quando_CriaTask_Entao_Recusa()
    {
        _tasks.ListByIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), "conv-1", Arg.Any<CancellationToken>())
            .Returns([TaskDto("task-1", status: DelegationTaskStatus.Running)]);

        var ex = await Should.ThrowAsync<DomainException>(() =>
            _service.CreateTaskAsync(Request(retryOf: "task-1")));
        ex.Message.ShouldContain("not retryable");
    }

    [Fact]
    public async Task Dado_RetryDeTaskFalha_Quando_CriaTask_Entao_Cria()
    {
        _tasks.ListByIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), "conv-1", Arg.Any<CancellationToken>())
            .Returns([TaskDto("task-1", status: DelegationTaskStatus.Failed)]);
        _tasks.AddAsync(Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => TaskDto("task-2"));

        var task = await _service.CreateTaskAsync(Request(retryOf: "task-1"));

        task.Id.ShouldBe("task-2");
    }

    // ---- dependency resolution ----

    [Fact]
    public async Task Dado_DepConcluida_Quando_ResolveDeps_Entao_PromoveReady()
    {
        var pending = TaskDto("task-2", dependsOn: ["task-1"]);
        _tasks.ListOpenAsync(Arg.Any<CancellationToken>()).Returns([pending]);
        _tasks.ListByIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), "conv-1", Arg.Any<CancellationToken>())
            .Returns([TaskDto("task-1", status: DelegationTaskStatus.Done)]);

        await _service.ResolveDependenciesAsync();

        await _tasks.Received(1).MarkReadyAsync("task-2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_DepViva_Quando_ResolveDeps_Entao_MantemPending()
    {
        var pending = TaskDto("task-2", dependsOn: ["task-1"]);
        _tasks.ListOpenAsync(Arg.Any<CancellationToken>()).Returns([pending]);
        _tasks.ListByIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), "conv-1", Arg.Any<CancellationToken>())
            .Returns([TaskDto("task-1", status: DelegationTaskStatus.Running)]);

        await _service.ResolveDependenciesAsync();

        await _tasks.DidNotReceive().MarkReadyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _tasks.DidNotReceive().FinalizeAsync(
            Arg.Any<string>(), Arg.Any<DelegationTaskStatus>(),
            Arg.Any<string?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_DepFalhou_Quando_ResolveDeps_Entao_Cancela()
    {
        var pending = TaskDto("task-2", dependsOn: ["task-1"]);
        _tasks.ListOpenAsync(Arg.Any<CancellationToken>()).Returns([pending]);
        _tasks.ListByIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), "conv-1", Arg.Any<CancellationToken>())
            .Returns([TaskDto("task-1", status: DelegationTaskStatus.Failed)]);

        await _service.ResolveDependenciesAsync();

        await _tasks.Received(1).FinalizeAsync(
            "task-2", DelegationTaskStatus.Cancelled,
            Arg.Is<string?>(s => s!.Contains("task-1")),
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    // ---- finalize + mailbox ----

    [Fact]
    public async Task Dado_RunSucesso_Quando_Finaliza_Entao_PostaWorkerDone()
    {
        _tasks.FinalizeAsync("task-1", DelegationTaskStatus.Done,
                Arg.Any<string?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(TaskDto("task-1", status: DelegationTaskStatus.Done));

        await _service.FinishRunAsync("task-1", succeeded: true, "ok");

        await _mailbox.Received(1).AddAsync(
            Arg.Is<PostMailboxMessageRequest>(m =>
                m.Kind == AgentMailboxKinds.WorkerDone
                && m.Scope == "conv-1"
                && m.Payload.Contains("task-1")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_RunFalha_Quando_Finaliza_Entao_PostaEscalation()
    {
        _tasks.FinalizeAsync("task-1", DelegationTaskStatus.Failed,
                Arg.Any<string?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(TaskDto("task-1", status: DelegationTaskStatus.Failed));

        await _service.FinishRunAsync("task-1", succeeded: false, "boom");

        await _mailbox.Received(1).AddAsync(
            Arg.Is<PostMailboxMessageRequest>(m => m.Kind == AgentMailboxKinds.Escalation),
            Arg.Any<CancellationToken>());
    }

    // ---- heartbeat sweep ----

    [Fact]
    public async Task Dado_HeartbeatVelho_Quando_Sweep_Entao_FalhaTask()
    {
        var running = TaskDto("task-1", status: DelegationTaskStatus.Running) with
        {
            LastHeartbeatAt = DateTime.UtcNow - TimeSpan.FromHours(1),
        };
        _tasks.ListOpenAsync(Arg.Any<CancellationToken>()).Returns([running]);
        _tasks.FinalizeAsync("task-1", DelegationTaskStatus.Failed,
                Arg.Any<string?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(running with { Status = DelegationTaskStatus.Failed });

        var swept = await _service.SweepStaleHeartbeatsAsync(
            TimeSpan.FromMinutes(5), DateTime.UtcNow);

        swept.ShouldBe(1);
        await _tasks.Received(1).FinalizeAsync(
            "task-1", DelegationTaskStatus.Failed,
            Arg.Is<string?>(s => s!.Contains("heartbeat")),
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_HeartbeatFresco_Quando_Sweep_Entao_Mantem()
    {
        var running = TaskDto("task-1", status: DelegationTaskStatus.Running) with
        {
            LastHeartbeatAt = DateTime.UtcNow,
        };
        _tasks.ListOpenAsync(Arg.Any<CancellationToken>()).Returns([running]);

        var swept = await _service.SweepStaleHeartbeatsAsync(
            TimeSpan.FromMinutes(5), DateTime.UtcNow);

        swept.ShouldBe(0);
    }

    // ---- inbox ----

    [Fact]
    public async Task Dado_MsgNaoLida_Quando_LeInbox_Entao_MarcaLida()
    {
        var msg = new MailboxMessageDto(
            "msg-1", "conv-1", "codex", "@all", AgentMailboxKinds.WorkerDone,
            "done", DateTime.UtcNow, null);
        _mailbox.ListForRecipientAsync(
                "conv-1", Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<int>(), true, Arg.Any<CancellationToken>())
            .Returns([msg]);

        var read = await _service.ReadInboxAsync("conv-1", ["@all"]);

        await _mailbox.Received(1).MarkReadAsync(
            Arg.Is<IReadOnlyCollection<string>>(ids => ids.Contains("msg-1")),
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        read[0].ReadAt.ShouldNotBeNull();
    }

    // ---- reply/dismiss (SPEC-20261007 RF-001) ----

    private static MailboxMessageDto Message(
        string id = "msg-1", string scope = "conv-1", string from = "codex") =>
        new(id, scope, from, "@all", AgentMailboxKinds.Escalation,
            "need input", DateTime.UtcNow, null);

    [Fact]
    public async Task Dado_Escalation_Quando_Responde_Entao_PostaTextoParaRemetenteEMarcaLida()
    {
        _mailbox.GetAsync("msg-1", Arg.Any<CancellationToken>()).Returns(Message());
        _mailbox.AddAsync(Arg.Any<PostMailboxMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => new MailboxMessageDto(
                "msg-2", "conv-1", "human", "codex", AgentMailboxKinds.Text,
                ci.Arg<PostMailboxMessageRequest>().Payload, DateTime.UtcNow, null));

        var reply = await _service.ReplyMailboxAsync("conv-1", "msg-1", "human", "do the thing");

        reply.ShouldNotBeNull();
        await _mailbox.Received(1).AddAsync(
            Arg.Is<PostMailboxMessageRequest>(m =>
                m.Kind == AgentMailboxKinds.Text
                && m.FromAgent == "human"
                && m.ToAgent == "codex"
                && m.Payload.Contains("do the thing")),
            Arg.Any<CancellationToken>());
        await _mailbox.Received(1).MarkReadAsync(
            Arg.Is<IReadOnlyCollection<string>>(ids => ids.Contains("msg-1")),
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_MsgInexistente_Quando_Responde_Entao_NullSemPostar()
    {
        _mailbox.GetAsync("msg-x", Arg.Any<CancellationToken>()).Returns((MailboxMessageDto?)null);

        var reply = await _service.ReplyMailboxAsync("conv-1", "msg-x", "human", "oi");

        reply.ShouldBeNull();
        await _mailbox.DidNotReceive().AddAsync(
            Arg.Any<PostMailboxMessageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_MsgDeOutroEscopo_Quando_Responde_Entao_NullSemPostar()
    {
        _mailbox.GetAsync("msg-1", Arg.Any<CancellationToken>())
            .Returns(Message(scope: "other-scope"));

        var reply = await _service.ReplyMailboxAsync("conv-1", "msg-1", "human", "oi");

        reply.ShouldBeNull();
        await _mailbox.DidNotReceive().AddAsync(
            Arg.Any<PostMailboxMessageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_CorpoVazio_Quando_Responde_Entao_Recusa()
    {
        await Should.ThrowAsync<DomainException>(() =>
            _service.ReplyMailboxAsync("conv-1", "msg-1", "human", "   "));
    }

    [Fact]
    public async Task Dado_IdsNoEscopo_Quando_Dispensa_Entao_MarcaLidasERetornaContagem()
    {
        _mailbox.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Message(id: ci.Arg<string>()));

        var dismissed = await _service.DismissMailboxAsync("conv-1", ["msg-1", "msg-2"]);

        dismissed.ShouldBe(2);
        await _mailbox.Received(1).MarkReadAsync(
            Arg.Is<IReadOnlyCollection<string>>(ids => ids.Count == 2),
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_IdsDeOutroEscopo_Quando_Dispensa_Entao_IgnoraERetornaZero()
    {
        _mailbox.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Message(id: ci.Arg<string>(), scope: "other"));

        var dismissed = await _service.DismissMailboxAsync("conv-1", ["msg-1"]);

        dismissed.ShouldBe(0);
        await _mailbox.DidNotReceive().MarkReadAsync(
            Arg.Any<IReadOnlyCollection<string>>(),
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
}
