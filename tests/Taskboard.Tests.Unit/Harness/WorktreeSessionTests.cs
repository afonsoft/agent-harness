using Shouldly;
using Taskboard;
using Taskboard.Domain.Entities.Harness;
using Taskboard.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

public class WorktreeSessionTests
{
    // Covers RF-001: CreateWorktreeAsync → WorktreeSession(Path, Branch, Status.Active)
    [Fact]
    public void Dado_ParametrosValidos_Quando_Create_Entao_SessaoAtivaComCaminhos()
    {
        var session = WorktreeSession.Create(
            WorktreeSessionId.NewGuid(),
            runId: "run_01j7abcde",
            repositoryPath: "/home/ubuntu/repos/agent-harness",
            baseBranch: "main",
            path: "/home/ubuntu/.taskboard/worktrees/run_01j7abcde",
            branch: "feature/agent-run_01j7abcde-fix-login-error");

        session.Status.ShouldBe(WorktreeStatus.Active);
        session.RunId.ShouldBe("run_01j7abcde");
        session.RepositoryPath.ShouldBe("/home/ubuntu/repos/agent-harness");
        session.BaseBranch.ShouldBe("main");
        session.Path.ShouldBe("/home/ubuntu/.taskboard/worktrees/run_01j7abcde");
        session.Branch.ShouldBe("feature/agent-run_01j7abcde-fix-login-error");
        session.CommitSha.ShouldBeNull();
        session.Version.ShouldBe(1);
    }

    [Fact]
    public void Dado_RunIdVazio_Quando_Create_Entao_LancaDomainException()
    {
        Should.Throw<DomainException>(() => WorktreeSession.Create(
            WorktreeSessionId.NewGuid(),
            runId: " ",
            repositoryPath: "/repo",
            baseBranch: "main",
            path: "/wt",
            branch: "b"));
    }

    [Fact]
    public void Dado_CaminhoVazio_Quando_Create_Entao_LancaDomainException()
    {
        Should.Throw<DomainException>(() => WorktreeSession.Create(
            WorktreeSessionId.NewGuid(),
            runId: "run_1",
            repositoryPath: "/repo",
            baseBranch: "main",
            path: "",
            branch: "b"));
    }

    // Covers RF-003: CommitAsync → CommitSha registrado na sessão
    [Fact]
    public void Dado_SessaoAtiva_Quando_RecordCommit_Entao_CommitShaRegistrado()
    {
        var session = NovaSessao();

        session.RecordCommit("abc1234");

        session.CommitSha.ShouldBe("abc1234");
        session.Version.ShouldBe(2);
    }

    [Fact]
    public void Dado_SessaoRemovida_Quando_RecordCommit_Entao_LancaDomainException()
    {
        var session = NovaSessao();
        session.MarkRemoved();

        Should.Throw<DomainException>(() => session.RecordCommit("abc1234"));
    }

    // Covers RF-004: teardown → Status.Removed
    [Fact]
    public void Dado_SessaoAtiva_Quando_MarkRemoved_Entao_StatusRemoved()
    {
        var session = NovaSessao();

        session.MarkRemoved();

        session.Status.ShouldBe(WorktreeStatus.Removed);
        session.Version.ShouldBe(2);
    }

    [Fact]
    public void Dado_SessaoRemovida_Quando_MarkRemoved_Entao_LancaDomainException()
    {
        var session = NovaSessao();
        session.MarkRemoved();

        Should.Throw<DomainException>(() => session.MarkRemoved());
    }

    // Covers RF-004: falha + RetainOnFailure → RetainedForInspection
    [Fact]
    public void Dado_SessaoFalha_Quando_MarkRetainedForInspection_Entao_StatusRetained()
    {
        var session = NovaSessao();
        session.MarkFailed();

        session.MarkRetainedForInspection();

        session.Status.ShouldBe(WorktreeStatus.RetainedForInspection);
    }

    [Fact]
    public void Dado_SessaoAtiva_Quando_MarkRetainedForInspection_Entao_LancaDomainException()
    {
        var session = NovaSessao();

        Should.Throw<DomainException>(() => session.MarkRetainedForInspection());
    }

    [Fact]
    public void Dado_SessaoAtiva_Quando_MarkCompleted_Entao_StatusCompleted()
    {
        var session = NovaSessao();

        session.MarkCompleted();

        session.Status.ShouldBe(WorktreeStatus.Completed);
    }

    [Fact]
    public void Dado_RetainOnFailure_Quando_Create_Entao_FlagPersistida()
    {
        var session = WorktreeSession.Create(
            WorktreeSessionId.NewGuid(),
            runId: "run_1",
            repositoryPath: "/repo",
            baseBranch: "main",
            path: "/wt",
            branch: "b",
            retainOnFailure: true);

        session.RetainOnFailure.ShouldBeTrue();
    }

    private static WorktreeSession NovaSessao() => WorktreeSession.Create(
        WorktreeSessionId.NewGuid(),
        runId: "run_1",
        repositoryPath: "/repo",
        baseBranch: "main",
        path: "/wt/run_1",
        branch: "feature/agent-run_1-x");
}
