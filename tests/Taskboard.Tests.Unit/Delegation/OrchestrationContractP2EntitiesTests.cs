using Shouldly;
using Taskboard.Domain.Entities.Delegation;
using Taskboard.Delegation;
using Xunit;

namespace Taskboard.Tests.Unit.Delegation;

/// <summary>
/// SPEC-20261020-orchestration-contract-v2 P2: PendingQuestion (RF-004) e
/// DelegationGate (RF-005) — invariantes e transições.
/// </summary>
public class OrchestrationContractP2EntitiesTests
{
    // ---- PendingQuestion ----

    [Fact]
    public void Dado_PendingQuestion_Quando_Cria_Entao_Pendente()
    {
        var q = PendingQuestion.Create("conv-1", "run-1", "msg-1", "codex", "qual opção?", "[\"a\",\"b\"]");

        q.Id.ShouldStartWith("q-");
        q.Status.ShouldBe(PendingQuestionStatus.Pending);
        q.Answer.ShouldBeNull();
        q.ResolvedAtUtc.ShouldBeNull();
        q.Version.ShouldBe(0);
    }

    [Fact]
    public void Dado_PendingQuestion_Quando_Answer_Entao_AnsweredComTexto()
    {
        var q = PendingQuestion.Create("conv-1", "run-1", "msg-1", "codex", "prosigo?", null);

        q.MarkAnswered("sim");

        q.Status.ShouldBe(PendingQuestionStatus.Answered);
        q.Answer.ShouldBe("sim");
        q.ResolvedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public void Dado_QuestionAnswered_Quando_AnswerDeNovo_Entao_QuestionNotPending()
    {
        var q = PendingQuestion.Create("conv-1", "run-1", "msg-1", "codex", "prosigo?", null);
        q.MarkAnswered("sim");

        var ex = Should.Throw<DomainException>(() => q.MarkAnswered("de novo"));
        ex.Message.ShouldContain("question_not_pending");
    }

    [Fact]
    public void Dado_QuestionTimedOut_Quando_Resume_Entao_VoltaPendingSemDuplicar()
    {
        var q = PendingQuestion.Create("conv-1", "run-1", "msg-1", "codex", "prosigo?", null);
        q.TimeOut();

        q.Resume();

        q.Status.ShouldBe(PendingQuestionStatus.Pending);
        q.MessageId.ShouldBe("msg-1");
    }

    [Fact]
    public void Dado_QuestionAnswered_Quando_Resume_Entao_QuestionNotPending()
    {
        var q = PendingQuestion.Create("conv-1", "run-1", "msg-1", "codex", "prosigo?", null);
        q.MarkAnswered("ok");

        Should.Throw<DomainException>(() => q.Resume());
    }

    [Fact]
    public void Dado_QuestionVazia_Quando_Cria_Entao_Recusa()
    {
        Should.Throw<DomainException>(() =>
            PendingQuestion.Create("conv-1", "run-1", "msg-1", "codex", " ", null));
    }

    // ---- DelegationGate ----

    [Fact]
    public void Dado_Gate_Quando_Cria_Entao_OpenNaTask()
    {
        var g = DelegationGate.Create("task-1", "conv-1", "aplicar migração?", "[\"sim\",\"não\"]");

        g.Id.ShouldStartWith("gate-");
        g.TaskId.ShouldBe("task-1");
        g.Status.ShouldBe(DelegationGateStatus.Open);
        g.Resolution.ShouldBeNull();
    }

    [Fact]
    public void Dado_GateOpen_Quando_Resolve_Entao_ResolvedComResolution()
    {
        var g = DelegationGate.Create("task-1", "conv-1", "aplicar?", null);

        g.Resolve("sim");

        g.Status.ShouldBe(DelegationGateStatus.Resolved);
        g.Resolution.ShouldBe("sim");
        g.ResolvedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public void Dado_GateResolved_Quando_ResolveDeNovo_Entao_Recusa()
    {
        var g = DelegationGate.Create("task-1", "conv-1", "aplicar?", null);
        g.Resolve("sim");

        Should.Throw<DomainException>(() => g.Resolve("não"));
    }

    [Fact]
    public void Dado_GateSemTask_Quando_Cria_Entao_Recusa()
    {
        Should.Throw<DomainException>(() => DelegationGate.Create(" ", "conv-1", "pergunta", null));
    }
}
