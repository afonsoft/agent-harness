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
/// SPEC-20261020-orchestration-contract-v2 (P2): ask/reply (RF-004), gates
/// task-bound (RF-005), task_update blocked (RF-010) e group addresses (RF-009).
/// </summary>
public class OrchestrationContractP2ServiceTests
{
    private readonly IDelegationTaskRepository _tasks = Substitute.For<IDelegationTaskRepository>();
    private readonly IAgentMailboxRepository _mailbox = Substitute.For<IAgentMailboxRepository>();
    private readonly IDelegationRunRepository _runs = Substitute.For<IDelegationRunRepository>();
    private readonly IDispatchRepository _dispatches = Substitute.For<IDispatchRepository>();
    private readonly IDeliveryRepository _deliveries = Substitute.For<IDeliveryRepository>();
    private readonly IPendingQuestionRepository _questions = Substitute.For<IPendingQuestionRepository>();
    private readonly IDelegationGateRepository _gates = Substitute.For<IDelegationGateRepository>();
    private readonly DelegationService _service;

    public OrchestrationContractP2ServiceTests()
    {
        _service = new DelegationService(
            _tasks, _mailbox, _runs, _dispatches, _deliveries,
            _questions, _gates, TimeProvider.System);
    }

    private static DelegationRunDto Run() =>
        new("run-1", "conv-1", "obj", "assistant", DelegationRunStatus.Open, DateTime.UtcNow, null);

    private static DelegationTaskDto Task(
        string id, DelegationTaskStatus status = DelegationTaskStatus.Pending) =>
        new(id, "conv-1", "prompt", "codex", [], null, null, false, null,
            "/ws", null, null, status, null, null,
            DateTime.UtcNow, null, null, null, RunId: "run-1");

    private static MailboxMessageDto Msg(string id) =>
        new(id, "conv-1", "codex", "@all", "question", "body", DateTime.UtcNow, null);

    private static PendingQuestionDto Question(
        string id = "q-1", PendingQuestionStatus status = PendingQuestionStatus.Pending,
        string messageId = "msg-1", string? answer = null) =>
        new(id, "conv-1", "run-1", messageId, "codex", "prosigo?", null,
            status, answer, DateTime.UtcNow, null);

    private static DelegationGateDto Gate(
        string id = "gate-1", DelegationGateStatus status = DelegationGateStatus.Open,
        string taskId = "task-1") =>
        new(id, taskId, "conv-1", "aplicar?", null, status, null, DateTime.UtcNow, null);

    // ---- RF-004: ask ----

    [Fact]
    public async Task Dado_AskSemResume_Quando_Timeout_Entao_PostQuestionEPendingTimedOut()
    {
        _runs.GetOpenByScopeAsync("conv-1", Arg.Any<CancellationToken>()).Returns(Run());
        _mailbox.AddAsync(Arg.Any<PostMailboxMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => Msg("msg-1") with
            {
                Payload = ((PostMailboxMessageRequest)ci[0]).Payload,
            });
        _questions.AddAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Question());
        _questions.GetAsync("q-1", Arg.Any<CancellationToken>()).Returns(Question());
        _questions.TimeOutAsync("q-1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Question(status: PendingQuestionStatus.TimedOut));

        var result = await _service.AskQuestionAsync(
            new AskQuestionRequest("conv-1", "codex", "prosigo?",
                Timeout: TimeSpan.FromMilliseconds(50)));

        result.Status.ShouldBe(PendingQuestionStatus.TimedOut);
        await _mailbox.Received(1).AddAsync(
            Arg.Is<PostMailboxMessageRequest>(r => r.Kind == AgentMailboxKinds.Question),
            Arg.Any<CancellationToken>());
        await _questions.Received(1).AddAsync(
            "conv-1", "run-1", "msg-1", "codex", "prosigo?",
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_AskComResume_Quando_TimedOut_Entao_ReabreSemPostarDeNovo()
    {
        _questions.GetByMessageAsync("msg-1", Arg.Any<CancellationToken>())
            .Returns(Question(status: PendingQuestionStatus.TimedOut));
        _questions.ResumeAsync("q-1", Arg.Any<CancellationToken>())
            .Returns(Question());
        _questions.GetAsync("q-1", Arg.Any<CancellationToken>()).Returns(Question());
        _questions.TimeOutAsync("q-1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Question(status: PendingQuestionStatus.TimedOut));

        var result = await _service.AskQuestionAsync(
            new AskQuestionRequest("conv-1", "codex", "prosigo?",
                Timeout: TimeSpan.FromMilliseconds(50), ResumeMessageId: "msg-1"));

        result.Status.ShouldBe(PendingQuestionStatus.TimedOut);
        await _questions.Received(1).ResumeAsync("q-1", Arg.Any<CancellationToken>());
        await _mailbox.DidNotReceive().AddAsync(
            Arg.Any<PostMailboxMessageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_AskComResume_Quando_JaAnswered_Entao_RetornaRespostaSemBloquear()
    {
        _questions.GetByMessageAsync("msg-1", Arg.Any<CancellationToken>())
            .Returns(Question(status: PendingQuestionStatus.Answered, answer: "sim"));

        var result = await _service.AskQuestionAsync(
            new AskQuestionRequest("conv-1", "codex", "prosigo?", ResumeMessageId: "msg-1"));

        result.Status.ShouldBe(PendingQuestionStatus.Answered);
        result.Answer.ShouldBe("sim");
        await _questions.DidNotReceive().ResumeAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_Reply_Quando_Pending_Entao_AnsweredEPostAnswerParaAsker()
    {
        _questions.GetByMessageAsync("msg-1", Arg.Any<CancellationToken>())
            .Returns(Question());
        _questions.AnswerAsync("q-1", "sim", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Question(status: PendingQuestionStatus.Answered, answer: "sim"));

        var result = await _service.ReplyQuestionAsync("conv-1", "msg-1", "sim", "assistant");

        result.Status.ShouldBe(PendingQuestionStatus.Answered);
        await _mailbox.Received(1).AddAsync(
            Arg.Is<PostMailboxMessageRequest>(r =>
                r.Kind == AgentMailboxKinds.Answer && r.ToAgent == "codex"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_Reply_Quando_JaResolvida_Entao_QuestionNotPending()
    {
        _questions.GetByMessageAsync("msg-1", Arg.Any<CancellationToken>())
            .Returns(Question(status: PendingQuestionStatus.Answered, answer: "sim"));

        var ex = await Should.ThrowAsync<DomainException>(() =>
            _service.ReplyQuestionAsync("conv-1", "msg-1", "não", "assistant"));
        ex.Message.ShouldContain("question_not_pending");
    }

    [Fact]
    public async Task Dado_ReplyDesconhecida_Quando_Reply_Entao_Recusa()
    {
        _questions.GetByMessageAsync("msg-x", Arg.Any<CancellationToken>())
            .Returns((PendingQuestionDto?)null);

        await Should.ThrowAsync<DomainException>(() =>
            _service.ReplyQuestionAsync("conv-1", "msg-x", "ok", "assistant"));
    }

    // ---- RF-005: gates ----

    [Fact]
    public async Task Dado_TaskPending_Quando_GateCreate_Entao_GateOpenETaskBlocked()
    {
        _tasks.GetAsync("task-1", Arg.Any<CancellationToken>()).Returns(Task("task-1"));
        _gates.AddAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Gate());

        var gate = await _service.CreateGateAsync("conv-1", "task-1", "aplicar?", ["sim", "não"]);

        gate.Status.ShouldBe(DelegationGateStatus.Open);
        await _tasks.Received(1).MarkBlockedAsync(
            "task-1", Arg.Is<string?>(r => r != null && r.Contains("gate-1")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_TaskTerminal_Quando_GateCreate_Entao_Recusa()
    {
        _tasks.GetAsync("task-1", Arg.Any<CancellationToken>())
            .Returns(Task("task-1", DelegationTaskStatus.Done));

        await Should.ThrowAsync<DomainException>(() =>
            _service.CreateGateAsync("conv-1", "task-1", "tarde demais"));
    }

    [Fact]
    public async Task Dado_GateOpen_Quando_Resolve_Entao_ResolvedETaskDesbloqueada()
    {
        _gates.GetAsync("gate-1", Arg.Any<CancellationToken>()).Returns(Gate());
        _gates.ResolveAsync("gate-1", "sim", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Gate(status: DelegationGateStatus.Resolved) with { Resolution = "sim" });
        _gates.HasOpenForTaskAsync("task-1", Arg.Any<CancellationToken>()).Returns(false);
        _tasks.UnblockAsync("task-1", Arg.Any<CancellationToken>()).Returns(Task("task-1"));

        var gate = await _service.ResolveGateAsync("conv-1", "gate-1", "sim");

        gate.Status.ShouldBe(DelegationGateStatus.Resolved);
        await _tasks.Received(1).UnblockAsync("task-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_GateComIrmaOpen_Quando_Resolve_Entao_TaskContinuaBlocked()
    {
        _gates.GetAsync("gate-1", Arg.Any<CancellationToken>()).Returns(Gate());
        _gates.ResolveAsync("gate-1", "sim", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Gate(status: DelegationGateStatus.Resolved) with { Resolution = "sim" });
        _gates.HasOpenForTaskAsync("task-1", Arg.Any<CancellationToken>()).Returns(true);

        await _service.ResolveGateAsync("conv-1", "gate-1", "sim");

        await _tasks.DidNotReceive().UnblockAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_GateOutroScope_Quando_Resolve_Entao_Recusa()
    {
        _gates.GetAsync("gate-1", Arg.Any<CancellationToken>())
            .Returns(Gate() with { Scope = "outra-conv" });

        await Should.ThrowAsync<DomainException>(() =>
            _service.ResolveGateAsync("conv-1", "gate-1", "sim"));
    }

    [Fact]
    public async Task Dado_Scope_Quando_ListGates_Entao_ListaDoScope()
    {
        _gates.ListByScopeAsync("conv-1", Arg.Any<CancellationToken>())
            .Returns([Gate("g1"), Gate("g2", DelegationGateStatus.Resolved)]);

        var list = await _service.ListGatesAsync("conv-1");

        list.Count.ShouldBe(2);
    }

    // ---- RF-010: task update blocked ----

    [Fact]
    public async Task Dado_TaskRunning_Quando_BlockTask_Entao_BlockedComResultJson()
    {
        _tasks.GetAsync("task-1", Arg.Any<CancellationToken>())
            .Returns(Task("task-1", DelegationTaskStatus.Running));
        _tasks.MarkBlockedAsync("task-1", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task("task-1", DelegationTaskStatus.Blocked));

        var task = await _service.BlockTaskAsync("conv-1", "task-1", """{"reason":"quota"}""");

        task!.Status.ShouldBe(DelegationTaskStatus.Blocked);
        await _tasks.Received(1).MarkBlockedAsync(
            "task-1", """{"reason":"quota"}""", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_TaskOutroScope_Quando_BlockTask_Entao_Null()
    {
        _tasks.GetAsync("task-1", Arg.Any<CancellationToken>())
            .Returns(Task("task-1") with { Scope = "outra" });

        var task = await _service.BlockTaskAsync("conv-1", "task-1");

        task.ShouldBeNull();
    }

    // ---- RF-009: group addresses ----

    [Theory]
    [InlineData("worker_done"), InlineData("heartbeat"), InlineData("decision")]
    public async Task Dado_KindLifecycle_Quando_PostParaGrupo_Entao_Recusa(string kind)
    {
        var ex = await Should.ThrowAsync<DomainException>(() =>
            _service.PostAsync(new PostMailboxMessageRequest(
                "conv-1", "codex", "@all", "payload", kind)));
        ex.Message.ShouldContain("group");
    }

    [Fact]
    public async Task Dado_Text_Quando_PostParaGrupo_Entao_Aceita()
    {
        _mailbox.AddAsync(Arg.Any<PostMailboxMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(Msg("msg-9"));

        var msg = await _service.PostAsync(new PostMailboxMessageRequest(
            "conv-1", "codex", "@worktree:run-7", "oi", "text"));

        msg.Id.ShouldBe("msg-9");
    }
}
