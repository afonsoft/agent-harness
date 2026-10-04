using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Delegation;
using Taskboard.Delegation;
using Taskboard.Dtos;
using Xunit;

namespace Taskboard.Tests.Unit.Delegation;

/// <summary>SPEC-20261006 RF-001: agregação das quatro colunas do dashboard.</summary>
public class DelegationDashboardServiceTests
{
    private readonly IDelegationTaskRepository _tasks = Substitute.For<IDelegationTaskRepository>();
    private readonly IAgentMailboxRepository _mailbox = Substitute.For<IAgentMailboxRepository>();
    private readonly IAgentOrchestrationService _orchestration = Substitute.For<IAgentOrchestrationService>();
    private readonly IAgentDiscoveryService _discovery = Substitute.For<IAgentDiscoveryService>();
    private readonly DelegationDashboardService _service;

    public DelegationDashboardServiceTests()
    {
        _service = new DelegationDashboardService(
            _tasks, _mailbox, _orchestration, _discovery, TimeProvider.System);
        _orchestration.GetLatestRunsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _discovery.DiscoverAsync(Arg.Any<CancellationToken>()).Returns([]);
    }

    private static DelegationTaskDto TaskDto(
        string id, DelegationTaskStatus status, string cli = "codex") =>
        new(id, "conv-1", "prompt " + id, cli, [], null, null, false, null,
            "/ws", null, null, status, null, null,
            DateTime.UtcNow, null, null, null);

    [Fact]
    public async Task Dado_TarefasPorEstado_Quando_Dashboard_Entao_ColunasCorretas()
    {
        _tasks.ListByScopeAsync("conv-1", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                TaskDto("t-run", DelegationTaskStatus.Running),
                TaskDto("t-fail", DelegationTaskStatus.Failed),
                TaskDto("t-done", DelegationTaskStatus.Done),
            ]);
        _mailbox.ListForRecipientAsync("conv-1", Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<int>(), true, Arg.Any<CancellationToken>())
            .Returns([new MailboxMessageDto(
                "m-esc", "conv-1", "codex", "@all", AgentMailboxKinds.Escalation,
                "boom", DateTime.UtcNow, null)]);

        var dashboard = await _service.GetAsync("conv-1");

        dashboard.Working.ShouldContain(i => i.Id == "t-run");
        dashboard.NeedsYou.ShouldContain(i => i.Id == "t-fail");
        dashboard.NeedsYou.ShouldContain(i => i.Id == "m-esc" && i.Kind == "mailbox");
        dashboard.Done.ShouldContain(i => i.Id == "t-done");
    }

    [Fact]
    public async Task Dado_RunAtivo_Quando_Dashboard_Entao_EmWorking()
    {
        _tasks.ListByScopeAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _orchestration.GetLatestRunsAsync(Arg.Any<CancellationToken>())
            .Returns([new AgentRunDto(
                Guid.NewGuid(), "issue-9", AgentType.Codex,
                AgentRunState.Running, DateTimeOffset.UtcNow, null)]);

        var dashboard = await _service.GetAsync("conv-1");

        dashboard.Working.ShouldContain(i => i.Kind == "run" && i.Title == "issue-9");
    }

    [Fact]
    public async Task Dado_CliLivre_Quando_Dashboard_Entao_EmIdle()
    {
        _tasks.ListByScopeAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([TaskDto("t-run", DelegationTaskStatus.Running, cli: "Codex")]);
        _discovery.DiscoverAsync(Arg.Any<CancellationToken>())
            .Returns([
                new AgentInfo("Codex", "/bin/codex", AgentType.Codex, AgentStatus.Available, null, null),
                new AgentInfo("Claude", "/bin/claude", AgentType.Claude, AgentStatus.Available, null, null),
            ]);

        var dashboard = await _service.GetAsync("conv-1");

        dashboard.Idle.ShouldContain(i => i.Id == "Claude");
        dashboard.Idle.ShouldNotContain(i => i.Id == "Codex");
    }
}
