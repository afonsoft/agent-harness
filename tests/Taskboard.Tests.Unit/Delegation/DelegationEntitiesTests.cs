using Shouldly;
using Taskboard;
using Taskboard.Delegation;
using Taskboard.Domain.Entities.Delegation;
using Xunit;

namespace Taskboard.Tests.Unit.Delegation;

/// <summary>SPEC-20261005 RF-001/RF-002: invariantes das entidades do DAG e do mailbox.</summary>
public class DelegationEntitiesTests
{
    [Fact]
    public void Dado_CreateValido_Quando_CriaTask_Entao_PendingECampos()
    {
        var task = DelegationTask.Create(
            "fix the bug", "codex", "conv-1", "/ws",
            dependsOn: ["task-0"], fanoutGroupId: "fan-1");

        task.Id.ShouldStartWith("task-");
        task.Status.ShouldBe(DelegationTaskStatus.Pending);
        task.DependsOn.ShouldBe(["task-0"]);
        task.FanoutGroupId.ShouldBe("fan-1");
        task.IsTerminal.ShouldBeFalse();
    }

    [Fact]
    public void Dado_PromptVazio_Quando_CriaTask_Entao_Recusa()
    {
        Should.Throw<DomainException>(() =>
            DelegationTask.Create(" ", "codex", "conv-1", "/ws"));
    }

    [Fact]
    public void Dado_WorktreeSemRepo_Quando_CriaTask_Entao_Recusa()
    {
        var ex = Should.Throw<DomainException>(() =>
            DelegationTask.Create("p", "codex", "conv-1", "/ws", useWorktree: true));
        ex.Message.ShouldContain("repositoryPath");
    }

    [Fact]
    public void Dado_TaskPronta_Quando_MarkReadyRodando_Entao_CicloDeVida()
    {
        var task = DelegationTask.Create("p", "codex", "conv-1", "/ws");

        task.MarkReady();
        task.Status.ShouldBe(DelegationTaskStatus.Ready);

        task.MarkRunning(DateTime.UtcNow.AddMinutes(-5));
        task.Status.ShouldBe(DelegationTaskStatus.Running);
        task.StartedAt.ShouldNotBeNull();
        task.LastHeartbeatAt.ShouldBe(task.StartedAt);

        task.Heartbeat();
        task.LastHeartbeatAt!.Value.ShouldBeGreaterThan(task.StartedAt!.Value);

        task.MarkDone("summary");
        task.IsTerminal.ShouldBeTrue();
        task.ResultSummary.ShouldBe("summary");
    }

    [Fact]
    public void Dado_TaskFalha_Quando_VerificaRetry_Entao_Retryavel()
    {
        var task = DelegationTask.Create("p", "codex", "conv-1", "/ws");

        task.IsRetryable.ShouldBeFalse();
        task.MarkFailed("boom");
        task.IsRetryable.ShouldBeTrue();
        task.MarkCancelled("x");
        task.IsRetryable.ShouldBeTrue();
    }

    [Fact]
    public void Dado_PayloadVazio_Quando_CriaMensagem_Entao_Recusa()
    {
        Should.Throw<DomainException>(() =>
            AgentMailboxMessage.Create("conv-1", "a", "@all", " "));
    }

    [Fact]
    public void Dado_Mensagem_Quando_MarcaLidaDuasVezes_Entao_MantemPrimeiraData()
    {
        var msg = AgentMailboxMessage.Create("conv-1", "codex", "@all", "hello");
        msg.MarkRead(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        msg.MarkRead(new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc));

        msg.ReadAt.ShouldBe(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }
}
