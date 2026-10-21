using NSubstitute;
using Shouldly;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Application.Harness;
using Taskboard.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

/// <summary>
/// SPEC-20261020-worktree-shared-context RF-006/RF-007: card upsert semantics —
/// explicit status stored directly; null/unknown probes git; repo path resolves
/// from the worktree's git-common-dir; notifier is best-effort.
/// </summary>
public class WorktreeCardServiceTests
{
    private readonly IWorktreeCardRepository _cards =
        Substitute.For<IWorktreeCardRepository>();
    private readonly IWorktreeStatusProbe _probe =
        Substitute.For<IWorktreeStatusProbe>();
    private readonly IWorktreeCardNotifier _notifier =
        Substitute.For<IWorktreeCardNotifier>();

    private WorktreeCardService Sut() =>
        new(_cards, _probe, _notifier);

    private void RepoPathOk(string repo = "/repo") =>
        _probe.ResolveRepoPathAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(repo);

    private WorktreeCardDto EchoedUpsert(
        string path, string repo, string? comment,
        WorkspaceStatus status, string? taskId) =>
        new(path, repo, comment, status, taskId,
            DateTime.UtcNow, DateTime.UtcNow);

    [Fact]
    public async Task Dado_StatusExplicito_Quando_Set_Entao_NaoProba()
    {
        RepoPathOk();
        _cards.UpsertAsync(
                "/wt", "/repo", "note", WorkspaceStatus.Dirty, "t1",
                Arg.Any<CancellationToken>())
            .Returns(EchoedUpsert("/wt", "/repo", "note", WorkspaceStatus.Dirty, "t1"));

        var card = await Sut().SetCardAsync("/wt", "note", "dirty", "t1");

        card.WorkspaceStatus.ShouldBe(WorkspaceStatus.Dirty);
        await _probe.DidNotReceive().ProbeAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifier.Received(1).PushAsync(card, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown")]
    public async Task Dado_StatusVazio_Quando_Set_Entao_Proba(string? status)
    {
        RepoPathOk();
        _probe.ProbeAsync("/wt", Arg.Any<CancellationToken>())
            .Returns(WorkspaceStatus.Clean);
        _cards.UpsertAsync(
                "/wt", "/repo", null, WorkspaceStatus.Clean, null,
                Arg.Any<CancellationToken>())
            .Returns(EchoedUpsert("/wt", "/repo", null, WorkspaceStatus.Clean, null));

        var card = await Sut().SetCardAsync("/wt", null, status);

        card.WorkspaceStatus.ShouldBe(WorkspaceStatus.Clean);
        await _probe.Received(1).ProbeAsync("/wt", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_NaoGit_Quando_Set_Entao_InvalidValue()
    {
        _probe.ResolveRepoPathAsync("/plain", Arg.Any<CancellationToken>())
            .Returns((string?)null);

        await Should.ThrowAsync<DomainException>(
            () => Sut().SetCardAsync("/plain", "x", "clean"));
    }

    [Fact]
    public async Task Dado_StatusInvalido_Quando_Set_Entao_InvalidValue()
    {
        RepoPathOk();

        var ex = await Should.ThrowAsync<DomainException>(
            () => Sut().SetCardAsync("/wt", "x", "bogus"));
        ex.Message.ShouldContain("clean|dirty|conflict|unknown");
    }

    [Fact]
    public async Task Dado_NotifierFalha_Quando_Set_Entao_WriteNaoPerdido()
    {
        RepoPathOk();
        _cards.UpsertAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<WorkspaceStatus>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(EchoedUpsert("/wt", "/repo", null, WorkspaceStatus.Clean, null));
        _notifier.PushAsync(Arg.Any<WorktreeCardDto>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new IOException("hub down"));

        var card = await Sut().SetCardAsync("/wt", null, "clean");

        card.WorktreePath.ShouldBe("/wt"); // write survives a dead notifier
    }

    // ---------- SPEC-20261021-worktree-setup-scripts: SetSetupAsync ----------

    [Fact]
    public async Task Dado_SetupState_Quando_SetSetup_Entao_PersisteENotifica()
    {
        _cards.SetSetupAsync(
                "/wt", "/repo", "running", null, null,
                Arg.Any<CancellationToken>())
            .Returns(new WorktreeCardDto(
                "/wt", "/repo", null, WorkspaceStatus.Unknown, null,
                DateTime.UtcNow, DateTime.UtcNow, "running"));

        var card = await Sut().SetSetupAsync("/wt", "/repo", "running", null, null);

        card.SetupState.ShouldBe("running");
        await _cards.Received(1).SetSetupAsync(
            "/wt", "/repo", "running", null, null, Arg.Any<CancellationToken>());
        await _notifier.Received(1).PushAsync(card, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "/repo")]
    [InlineData("/wt", " ")]
    public async Task Dado_PathVazio_Quando_SetSetup_Entao_InvalidValue(
        string worktree, string repo)
    {
        var ex = await Should.ThrowAsync<DomainException>(
            () => Sut().SetSetupAsync(worktree, repo, "running", null, null));

        ex.Message.ShouldContain("required");
        await _cards.DidNotReceive().SetSetupAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_NotifierFalha_Quando_SetSetup_Entao_WriteNaoPerdido()
    {
        _cards.SetSetupAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new WorktreeCardDto(
                "/wt", "/repo", null, WorkspaceStatus.Unknown, null,
                DateTime.UtcNow, DateTime.UtcNow, "ok", 0));
        _notifier.PushAsync(Arg.Any<WorktreeCardDto>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new IOException("hub down"));

        var card = await Sut().SetSetupAsync("/wt", "/repo", "ok", 0, "tail");

        card.SetupState.ShouldBe("ok");
    }
}
