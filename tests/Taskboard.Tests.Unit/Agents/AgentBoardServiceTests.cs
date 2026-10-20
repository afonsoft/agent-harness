using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Delegation;
using Taskboard.Dtos;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20261020-agent-board-v2: projeção de cards por coluna, precedência do
/// attention model sobre o status da task, unread por readAtUtc e deep-link.
/// </summary>
public class AgentBoardServiceTests
{
    private readonly IDelegationTaskRepository _tasks = Substitute.For<IDelegationTaskRepository>();
    private readonly IAgentMailboxRepository _mailbox = Substitute.For<IAgentMailboxRepository>();
    private readonly IAgentOrchestrationService _orchestration = Substitute.For<IAgentOrchestrationService>();
    private readonly IAgentDiscoveryService _discovery = Substitute.For<IAgentDiscoveryService>();
    private readonly IAgentSessionStateRepository _states = Substitute.For<IAgentSessionStateRepository>();
    private readonly IAgentBoardReadMarkerRepository _markers = Substitute.For<IAgentBoardReadMarkerRepository>();
    private readonly AgentBoardService _service;

    public AgentBoardServiceTests()
    {
        _mailbox.ListByScopeAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _orchestration.GetLatestRunsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _discovery.DiscoverAsync(Arg.Any<CancellationToken>()).Returns([]);
        _states.ListAsync(Arg.Any<IReadOnlyCollection<string>?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _markers.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, DateTime>());
        _service = new AgentBoardService(
            _tasks, _mailbox, _orchestration, _discovery, _states, _markers);
    }

    private static DelegationTaskDto Task(
        string id, DelegationTaskStatus status, string prompt = "do the thing") =>
        new(id, "harness", prompt, "codex", [], null, null, false, null, "/ws",
            "/repos/acme", null, status, "result ok", null,
            DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(-4),
            status is DelegationTaskStatus.Done or DelegationTaskStatus.Failed
                ? DateTime.UtcNow.AddMinutes(-1) : null,
            DateTime.UtcNow.AddMinutes(-2));

    [Fact]
    public async Task Dado_TaskRunning_Quando_Snapshot_Entao_CardWorking()
    {
        _tasks.ListByScopeAsync("harness", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Task("task-1", DelegationTaskStatus.Running)]);

        var snapshot = await _service.GetSnapshotAsync("harness", "local");

        var card = snapshot.Working.ShouldHaveSingleItem();
        card.Id.ShouldBe("task:task-1");
        card.CliName.ShouldBe("codex");
        card.State.ShouldBe(AgentAttentionState.Working);
        card.RepoName.ShouldBe("acme");
        card.DeepLink.ShouldContain("focus=task-1");
    }

    [Fact]
    public async Task Dado_AttentionWaiting_Quando_Snapshot_Entao_NeedsYou()
    {
        _tasks.ListByScopeAsync("harness", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Task("task-2", DelegationTaskStatus.Running)]);
        _states.ListAsync(Arg.Any<IReadOnlyCollection<string>?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new AgentSessionStateDto(
                "task:task-2", "codex", AgentAttentionState.WaitingYou,
                "decision", DateTime.UtcNow, DateTime.UtcNow,
                AttentionSignalKind.Mailbox, "approve deploy?", 1, 0)]);

        var snapshot = await _service.GetSnapshotAsync("harness", "local");

        var card = snapshot.NeedsYou.ShouldHaveSingleItem();
        card.State.ShouldBe(AgentAttentionState.WaitingYou);
        card.PreviewText.ShouldContain("approve deploy?");
        snapshot.Working.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_TaskDone_Quando_Snapshot_Entao_ColunaDone()
    {
        _tasks.ListByScopeAsync("harness", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Task("task-3", DelegationTaskStatus.Done)]);

        var snapshot = await _service.GetSnapshotAsync("harness", "local");

        snapshot.Done.ShouldHaveSingleItem().State.ShouldBe(AgentAttentionState.Done);
    }

    [Fact]
    public async Task Dado_CardLido_Quando_Snapshot_Entao_UnreadFalso()
    {
        var readAt = DateTime.UtcNow;
        _tasks.ListByScopeAsync("harness", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Task("task-4", DelegationTaskStatus.Done)]);
        _markers.ListAsync("local", Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, DateTime> { ["task:task-4"] = readAt });

        var snapshot = await _service.GetSnapshotAsync("harness", "local");

        snapshot.Done.ShouldHaveSingleItem().Unread.ShouldBeFalse();
        snapshot.UnreadTotal.ShouldBe(0);
    }

    [Fact]
    public async Task Dado_CardNaoLido_Quando_Snapshot_Entao_UnreadEConta()
    {
        _tasks.ListByScopeAsync("harness", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Task("task-5", DelegationTaskStatus.Done)]);

        var snapshot = await _service.GetSnapshotAsync("harness", "local");

        snapshot.Done.ShouldHaveSingleItem().Unread.ShouldBeTrue();
        snapshot.UnreadTotal.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_MarkRead_Quando_Chamado_Entao_Persiste()
    {
        await _service.MarkReadAsync("local", "task:t1");

        await _markers.Received(1).MarkReadAsync(
            "local", "task:t1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    // RF-004: filtros da toolbar (texto/cli/estado/hasPr).
    [Fact]
    public async Task Dado_FiltroTexto_Quando_Snapshot_Entao_SomenteCardsQueCasam()
    {
        _tasks.ListByScopeAsync("harness", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                Task("task-6", DelegationTaskStatus.Running, "fix the login bug"),
                Task("task-7", DelegationTaskStatus.Running, "refactor billing"),
            ]);

        var snapshot = await _service.GetSnapshotAsync(
            "harness", "local", query: new AgentBoardQuery(Text: "login"));

        var card = snapshot.Working.ShouldHaveSingleItem();
        card.SessionTitle.ShouldContain("login");
    }

    [Fact]
    public async Task Dado_PrNoMailbox_Quando_FiltroHasPr_Entao_SoCardComPr()
    {
        _tasks.ListByScopeAsync("harness", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                Task("task-8", DelegationTaskStatus.Done),
                Task("task-9", DelegationTaskStatus.Done),
            ]);
        _mailbox.ListByScopeAsync("harness", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new MailboxMessageDto(
                "m1", "harness", "codex", "coordinator", "text",
                "task-8 done — https://github.com/acme/repo/pull/42",
                DateTime.UtcNow, null)]);

        var snapshot = await _service.GetSnapshotAsync(
            "harness", "local", query: new AgentBoardQuery(HasPr: true));

        var card = snapshot.Done.ShouldHaveSingleItem();
        card.Id.ShouldBe("task:task-8");
        card.PrUrl.ShouldBe("https://github.com/acme/repo/pull/42");
    }
}
