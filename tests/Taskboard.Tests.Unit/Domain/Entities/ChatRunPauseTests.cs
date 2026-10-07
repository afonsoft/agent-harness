using Shouldly;
using Taskboard.Domain.Entities.Chat;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.Domain.Entities;

/// <summary>
/// SPEC-20261012-chat-run-controls: ciclo de vida paused — Pause/Resume/
/// Requeue/Stop sobre a entidade <see cref="ChatRun"/>.
/// </summary>
public sealed class ChatRunPauseTests
{
    private static ChatRun NewRun()
    {
        var conversationId = ChatConversationId.From("conv-1");
        var messageId = ChatMessageId.From("msg-1");
        return ChatRun.Create(ChatRunId.From("run-1"), conversationId, messageId);
    }

    [Fact]
    public void Dado_RunQueued_Quando_Pause_Entao_PausedComPausedAt()
    {
        // RF-002/RF-003: queued→paused — sai da frente da FIFO sem perder o lugar.
        var run = NewRun();

        run.Pause();

        run.Status.ShouldBe(ChatRunStatus.Paused);
        run.PausedAt.ShouldNotBeNull();
        run.Status.IsActive.ShouldBeTrue("paused segue ativa para o chip");
    }

    [Fact]
    public void Dado_RunRunning_Quando_Pause_Entao_Paused()
    {
        var run = NewRun();
        run.Start();

        run.Pause();

        run.Status.ShouldBe(ChatRunStatus.Paused);
        run.PausedAt.ShouldNotBeNull();
    }

    [Fact]
    public void Dado_RunPausedDeQueued_Quando_Resume_Entao_VoltaQueued()
    {
        // RF-003: a run nunca começou — volta para a fila (re-drive da transcript).
        var run = NewRun();
        run.Pause();

        run.Resume();

        run.Status.ShouldBe(ChatRunStatus.Queued);
        run.PausedAt.ShouldBeNull();
    }

    [Fact]
    public void Dado_RunPausedDeRunning_Quando_Resume_Entao_VoltaRunning()
    {
        var run = NewRun();
        run.Start();
        run.Pause();

        run.Resume();

        run.Status.ShouldBe(ChatRunStatus.Running);
        run.PausedAt.ShouldBeNull();
    }

    [Fact]
    public void Dado_RunPaused_Quando_Requeue_Entao_Queued()
    {
        // Resume de uma paused sem executor vivo — re-entra na fila.
        var run = NewRun();
        run.Start();
        run.Pause();

        run.Requeue();

        run.Status.ShouldBe(ChatRunStatus.Queued);
        run.PausedAt.ShouldBeNull();
    }

    [Fact]
    public void Dado_RunPaused_Quando_Stop_Entao_Stopped()
    {
        // RF-005: stop alcança a run pausada.
        var run = NewRun();
        run.Pause();

        run.Stop();

        run.Status.ShouldBe(ChatRunStatus.Stopped);
        run.PausedAt.ShouldBeNull("Finish limpa o marcador de pausa");
    }

    [Fact]
    public void Dado_RunCompleted_Quando_Pause_Entao_DomainException()
    {
        var run = NewRun();
        run.Complete(10, 5);

        Should.Throw<DomainException>(() => run.Pause());
    }

    [Fact]
    public void Dado_RunRunning_Quando_Resume_Entao_DomainException()
    {
        var run = NewRun();
        run.Start();

        Should.Throw<DomainException>(() => run.Resume());
    }

    [Fact]
    public void Dado_RunQueued_Quando_Requeue_Entao_DomainException()
    {
        var run = NewRun();

        Should.Throw<DomainException>(() => run.Requeue());
    }

    [Fact]
    public void Dado_RunPaused_Quando_Fail_Entao_PausedAtLimpa()
    {
        // Pausada mas o dispatcher encerra por erro — Finish limpa PausedAt.
        var run = NewRun();
        run.Start();
        run.Pause();

        run.Fail("boom");

        run.Status.ShouldBe(ChatRunStatus.Failed);
        run.PausedAt.ShouldBeNull();
    }
}
