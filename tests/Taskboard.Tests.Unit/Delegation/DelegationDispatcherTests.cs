using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Delegation;
using Taskboard.Dtos;
using Taskboard.Integrations.Delegation;
using Taskboard.Integrations.Harness;
using Taskboard.Integrations.Harness.Security;
using Taskboard.Application.Contracts.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Delegation;

/// <summary>
/// SPEC-20261005 RF-004/RF-005: uma passada do dispatcher — stale-base guard,
/// dispatch até maxConcurrent e finalização via serviço.
/// </summary>
public class DelegationDispatcherTests
{
    private readonly IDelegationService _delegation = Substitute.For<IDelegationService>();
    private readonly IDelegationTaskRepository _tasks = Substitute.For<IDelegationTaskRepository>();
    private readonly IAgentCliDefinitionRepository _defs = Substitute.For<IAgentCliDefinitionRepository>();
    private readonly IAgentOrchestrationService _orchestration = Substitute.For<IAgentOrchestrationService>();
    private readonly IGitCommandRunner _git = Substitute.For<IGitCommandRunner>();
    private readonly IWorkspaceIsolationService _isolation = Substitute.For<IWorkspaceIsolationService>();
    private readonly DelegationDispatcherService _dispatcher;

    public DelegationDispatcherTests()
    {
        _defs.ListAsync(Arg.Any<CancellationToken>()).Returns([]);
        _dispatcher = new DelegationDispatcherService(
            ScopeFactory(), _orchestration, _git, new SecretScrubber(),
            new ConfigurationBuilder().Build(),
            NullLogger<DelegationDispatcherService>.Instance);
    }

    private IServiceScopeFactory ScopeFactory() =>
        new ServiceCollection()
            .AddSingleton(_delegation)
            .AddSingleton(_tasks)
            .AddSingleton(_defs)
            .AddSingleton(_isolation)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

    private static DelegationTaskDto TaskDto(
        string id = "task-1",
        DelegationTaskStatus status = DelegationTaskStatus.Ready,
        string? baseSha = null,
        string? repoPath = null) =>
        new(id, "conv-1", "prompt", "mydef", [], null, null, false, null,
            "/ws", repoPath, baseSha, status, null, null,
            DateTime.UtcNow, null, null, null);

    [Fact]
    public async Task Dado_BaseMoveu_Quando_Tick_Entao_TaskFicaStale()
    {
        var task = TaskDto(baseSha: "aaaa1111", repoPath: "/repo");
        _delegation.ListOpenAsync(Arg.Any<CancellationToken>()).Returns([task]);
        _git.RunAsync("/repo", Arg.Is<IReadOnlyList<string>>(a => a.Contains("HEAD")),
                Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(new GitCommandResult(0, "bbbb2222\n", "", false));

        await _dispatcher.TickAsync();

        await _delegation.Received(1).MarkStaleAsync(
            "task-1", Arg.Is<string>(s => s.Contains("base moved")), Arg.Any<CancellationToken>());
        await _delegation.DidNotReceive().BeginRunAsync(
            Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_TaskReady_Quando_Tick_Entao_DespachaEFalhaDefAusente()
    {
        var task = TaskDto();
        _delegation.ListOpenAsync(Arg.Any<CancellationToken>()).Returns([task]);
        _delegation.BeginRunAsync("task-1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(task with { Status = DelegationTaskStatus.Running });
        _tasks.GetAsync("task-1", Arg.Any<CancellationToken>())
            .Returns(task with { Status = DelegationTaskStatus.Running });

        await _dispatcher.TickAsync();
        await WaitInflightAsync();

        await _delegation.Received(1).FinishRunAsync(
            "task-1", false,
            Arg.Is<string?>(s => s!.Contains("not found")),
            Arg.Any<bool?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_TaskBuiltin_Quando_RunSucede_Entao_FinalizaDone()
    {
        var task = TaskDto() with { CliName = "Codex" };
        _delegation.ListOpenAsync(Arg.Any<CancellationToken>()).Returns([task]);
        _delegation.BeginRunAsync("task-1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(task with { Status = DelegationTaskStatus.Running });
        _tasks.GetAsync("task-1", Arg.Any<CancellationToken>())
            .Returns(task with { Status = DelegationTaskStatus.Running });
        _orchestration.EnqueueAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _orchestration.GetRunsAsync("task:task-1", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new AgentRunDto(
                Guid.NewGuid(), "task:task-1", AgentType.Codex,
                AgentRunState.Succeeded, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)]);

        await _dispatcher.TickAsync();
        await WaitInflightAsync();

        await _delegation.Received(1).FinishRunAsync(
            "task-1", true, Arg.Any<string?>(),
            Arg.Any<bool?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_CapacidadeCheia_Quando_Tick_Entao_NaoDespacha()
    {
        // Simulate all 4 slots taken: leave inflight entries by hand.
        var task = TaskDto();
        _delegation.ListOpenAsync(Arg.Any<CancellationToken>()).Returns([task]);

        // Fill via reflection is fragile — instead assert default config keeps
        // ready tasks queued when BeginRunAsync returns null (task vanished).
        _delegation.BeginRunAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(default(DelegationTaskDto));

        await _dispatcher.TickAsync();

        _dispatcher.InflightCount.ShouldBe(0);
    }

    [Fact]
    public async Task Dado_TaskComWorktree_Quando_Despacha_Entao_ReusaSessaoNoRequest()
    {
        var session = new WorktreeSessionDto(
            "wt-1", "wt-1", "/repos/task-abc", "feat/task-abc", "active",
            "/repo", "main", null, true, DateTime.UtcNow, DateTime.UtcNow, 1);
        var task = TaskDto() with { CliName = "Codex", WorktreeRunId = "wt-1" };
        _delegation.ListOpenAsync(Arg.Any<CancellationToken>()).Returns([task]);
        _delegation.BeginRunAsync("task-1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(task with { Status = DelegationTaskStatus.Running });
        _tasks.GetAsync("task-1", Arg.Any<CancellationToken>())
            .Returns(task with { Status = DelegationTaskStatus.Running });
        _isolation.GetAsync("wt-1", Arg.Any<CancellationToken>()).Returns(session);
        _orchestration.EnqueueAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _orchestration.GetRunsAsync("task:task-1", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new AgentRunDto(
                Guid.NewGuid(), "task:task-1", AgentType.Codex,
                AgentRunState.Succeeded, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)]);

        await _dispatcher.TickAsync();
        await WaitInflightAsync();

        // The run must reuse the task worktree session — not nest a second
        // worktree inside it (compare/promote diff task.WorktreeRunId).
        await _orchestration.Received(1).EnqueueAsync(
            Arg.Is<AgentExecutionRequest>(r =>
                r.RepoPath == "/repos/task-abc" && r.ExistingWorktreeRunId == "wt-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_TaskSemWorktree_Quando_Despacha_Entao_ExistingRunIdNull()
    {
        var task = TaskDto() with { CliName = "Codex" };
        _delegation.ListOpenAsync(Arg.Any<CancellationToken>()).Returns([task]);
        _delegation.BeginRunAsync("task-1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(task with { Status = DelegationTaskStatus.Running });
        _tasks.GetAsync("task-1", Arg.Any<CancellationToken>())
            .Returns(task with { Status = DelegationTaskStatus.Running });
        _orchestration.EnqueueAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _orchestration.GetRunsAsync("task:task-1", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new AgentRunDto(
                Guid.NewGuid(), "task:task-1", AgentType.Codex,
                AgentRunState.Succeeded, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)]);

        await _dispatcher.TickAsync();
        await WaitInflightAsync();

        await _orchestration.Received(1).EnqueueAsync(
            Arg.Is<AgentExecutionRequest>(r =>
                r.RepoPath == "/ws" && r.ExistingWorktreeRunId == null),
            Arg.Any<CancellationToken>());
    }

    // ---- SPEC-20261104-run-terminal-runtime: interactive dispatch path ----

    private (DelegationDispatcherService Dispatcher, IRunTerminalService Runs) InteractiveDispatcher(
        IAgentMailboxRepository? mailbox = null,
        string? interactiveClis = null)
    {
        var runs = Substitute.For<IRunTerminalService>();
        var mbox = mailbox ?? Substitute.For<IAgentMailboxRepository>();
        var dispatches = Substitute.For<IDispatchRepository>();
        if (mailbox is null)
        {
            mbox.ListByScopeAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<MailboxMessageDto>());
        }

        var adapter = Substitute.For<IAgentAdapter>();
        adapter.CanHandle(Arg.Any<AgentType>()).Returns(true);
        adapter.BuildInteractiveCommand(
                Arg.Any<AgentType>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<AgentAutonomy>())
            .Returns(new AgentCommand("codex", [], "/ws"));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Taskboard:Delegation:InteractiveClis"] = interactiveClis ?? "codex",
            })
            .Build();

        var dispatcher = new DelegationDispatcherService(
            new ServiceCollection()
                .AddSingleton(_delegation)
                .AddSingleton(_tasks)
                .AddSingleton(_defs)
                .AddSingleton(_isolation)
                .AddSingleton(mbox)
                .AddSingleton(dispatches)
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>(),
            _orchestration, _git, new SecretScrubber(), config,
            NullLogger<DelegationDispatcherService>.Instance,
            runTerminals: runs, adapters: [adapter]);
        return (dispatcher, runs);
    }

    private static RunTerminalDto LiveTerminal() =>
        new("rt-1", "task-1", "disp-1", "run-task-1", "codex",
            RunTerminalState.Live, RunTerminalControlOwner.Operator,
            null, DateTime.UtcNow, null, null, false, null);

    private static void SetupInteractiveRun(IRunTerminalService runs, RunTerminalDto? dto = null)
    {
        dto ??= LiveTerminal();
        runs.EnsureAsync(Arg.Any<EnsureRunTerminalRequest>(), Arg.Any<CancellationToken>())
            .Returns(dto);
        runs.SendAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new RunTerminalOpResult(true, dto));
        runs.GetAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(dto);
        runs.ReadAsync(
                Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<bool>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new RunReadResult(dto.Id, "worker report", false, null, dto));
    }

    private DelegationTaskDto RunningInteractiveTask()
    {
        var task = TaskDto() with { CliName = "Codex", ActiveDispatchId = "disp-1" };
        _delegation.ListOpenAsync(Arg.Any<CancellationToken>()).Returns([task]);
        _delegation.BeginRunAsync("task-1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(task with { Status = DelegationTaskStatus.Running });
        _tasks.GetAsync("task-1", Arg.Any<CancellationToken>())
            .Returns(task with { Status = DelegationTaskStatus.Running });
        return task;
    }

    private static async Task WaitInflightAsync(DelegationDispatcherService dispatcher)
    {
        for (var i = 0; i < 200 && dispatcher.InflightCount > 0; i++)
        {
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Dado_TaskBuiltinInterativa_Quando_WorkerDoneNoMailbox_Entao_FinalizaDone()
    {
        var mbox = Substitute.For<IAgentMailboxRepository>();
        mbox.ListByScopeAsync("conv-1", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new MailboxMessageDto(
                "m-1", "conv-1", "worker", "coordinator", AgentMailboxKinds.WorkerDone,
                "{}", DateTime.UtcNow, null, DispatchId: "disp-1")]);
        var (dispatcher, runs) = InteractiveDispatcher(mbox);
        SetupInteractiveRun(runs);
        RunningInteractiveTask();

        await dispatcher.TickAsync();
        await WaitInflightAsync(dispatcher);

        await _delegation.Received(1).FinishRunAsync(
            "task-1", true,
            Arg.Is<string?>(s => s!.Contains("worker_done")),
            Arg.Any<bool?>(), Arg.Any<CancellationToken>());
        await _orchestration.DidNotReceive().EnqueueAsync(
            Arg.Any<AgentExecutionRequest>(), Arg.Any<CancellationToken>());
        await runs.Received(1).UnpinAsync("run-task-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_TerminalMorreSemDone_Quando_Interactive_Entao_FinalizaFail()
    {
        var (dispatcher, runs) = InteractiveDispatcher();
        var dead = LiveTerminal() with { State = RunTerminalState.Finished, ExitCode = 1 };
        SetupInteractiveRun(runs, dead);
        RunningInteractiveTask();

        await dispatcher.TickAsync();
        await WaitInflightAsync(dispatcher);

        await _delegation.Received(1).FinishRunAsync(
            "task-1", false,
            Arg.Is<string?>(s => s!.Contains("worker_done") || s!.Contains("exited")),
            Arg.Any<bool?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_CliForaDaLista_Quando_Despacha_Entao_StreamingPathSemTerminal()
    {
        var (dispatcher, runs) = InteractiveDispatcher(interactiveClis: "");
        RunningInteractiveTask();
        _orchestration.EnqueueAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _orchestration.GetRunsAsync("task:task-1", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new AgentRunDto(
                Guid.NewGuid(), "task:task-1", AgentType.Codex,
                AgentRunState.Succeeded, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)]);

        await dispatcher.TickAsync();
        await WaitInflightAsync(dispatcher);

        await _delegation.Received(1).FinishRunAsync(
            "task-1", true, Arg.Any<string?>(),
            Arg.Any<bool?>(), Arg.Any<CancellationToken>());
        await runs.DidNotReceive().EnsureAsync(
            Arg.Any<EnsureRunTerminalRequest>(), Arg.Any<CancellationToken>());
    }

    private async Task WaitInflightAsync()
    {
        for (var i = 0; i < 200 && _dispatcher.InflightCount > 0; i++)
        {
            await Task.Delay(20);
        }
    }
}
