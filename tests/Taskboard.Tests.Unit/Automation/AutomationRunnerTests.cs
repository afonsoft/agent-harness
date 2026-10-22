using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Domain.Entities.Automation;
using Taskboard.Dtos;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.EntityFrameworkCore.Repositories;
using Taskboard.Integrations.Execution;
using Taskboard.Server.Automation;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.Automation;

/// <summary>SPEC-20261021-agent-automations RF-003: precheck gate, target
/// resolution and task creation on the shared fire path.</summary>
public sealed class AutomationRunnerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"tb-autorun-{Guid.NewGuid():N}.db");
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"tb-ws-{Guid.NewGuid():N}");
    private readonly List<TaskboardDbContext> _contexts = [];

    public void Dispose()
    {
        foreach (var ctx in _contexts)
        {
            ctx.Dispose();
        }
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    private TaskboardDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        var ctx = new TaskboardDbContext(options);
        ctx.Database.EnsureCreated();
        _contexts.Add(ctx);
        return ctx;
    }

    private AutomationRunner NewRunner(
        TaskboardDbContext ctx,
        IDelegationService? delegation = null,
        IProcessRunner? processes = null,
        IWorkspaceIsolationService? isolation = null,
        IConfiguration? configuration = null) =>
        new(
            new EfCoreRepository<AgentAutomation>(ctx),
            new EfCoreRepository<AgentAutomationRun>(ctx),
            delegation ?? Substitute.For<IDelegationService>(),
            processes ?? Substitute.For<IProcessRunner>(),
            configuration ?? Config(),
            NullLogger<AutomationRunner>.Instance,
            isolation: isolation);

    private IConfiguration Config()
    {
        Directory.CreateDirectory(_workspace);
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Taskboard:WorkspaceRoot"] = _workspace,
        }).Build();
    }

    private async Task<AgentAutomation> SeedAutomation(
        TaskboardDbContext ctx,
        string target,
        string? precheck = null)
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "demo"));
        var auto = AgentAutomation.Create(
            AgentAutomationId.NewGuid(),
            new AgentAutomationSpec(
                "test", "do the thing", "claude", target,
                AutomationScheduleKinds.Cron, "0 9 * * *", null,
                DateTime.UtcNow.AddHours(1), precheck, null, 60, false, null, "local"));
        var repo = new EfCoreRepository<AgentAutomation>(ctx);
        await repo.AddAsync(auto);
        await repo.SaveChangesAsync();
        return auto;
    }

    [Fact]
    public async Task Dado_PrecheckFalha_Quando_FireAsync_Entao_RunSkippedSemTask()
    {
        await using var ctx = NewContext();
        var auto = await SeedAutomation(ctx, "worktree:" + _workspace, precheck: "exit 3");
        var delegation = Substitute.For<IDelegationService>();
        var processes = Substitute.For<IProcessRunner>();
        processes.RunAsync("bash", Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ProcessRunResult(3, "", "nope", false)));
        var runner = NewRunner(ctx, delegation, processes);

        var run = await runner.FireAsync(auto.Id.Value);

        run.Outcome.ShouldBe("skipped");
        run.SkipReason.ShouldNotBeNull().ShouldContain("precheck exit 3");
        await delegation.DidNotReceive().CreateTaskAsync(
            Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_TargetInvalido_Quando_FireAsync_Entao_RunSkipped()
    {
        await using var ctx = NewContext();
        var auto = await SeedAutomation(ctx, "project:x"); // project: não resolvido no P1
        var runner = NewRunner(ctx);

        var run = await runner.FireAsync(auto.Id.Value);

        run.Outcome.ShouldBe("skipped");
        run.SkipReason.ShouldNotBeNull().ShouldContain("invalid_target");
    }

    [Fact]
    public async Task Dado_WorktreeTarget_Quando_FireAsync_Entao_CriaTaskSemWorktree()
    {
        await using var ctx = NewContext();
        var auto = await SeedAutomation(ctx, "worktree:" + _workspace);
        var delegation = Substitute.For<IDelegationService>();
        delegation.CreateTaskAsync(Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DelegationTaskDto(
                "task-1", $"automation:{auto.Id.Value}", "do the thing", "claude",
                [], null, null, false, null, _workspace, null, null,
                Taskboard.Delegation.DelegationTaskStatus.Ready,
                null, null, DateTime.UtcNow, null, null, null, Kind: "automation"));
        var runner = NewRunner(ctx, delegation);

        var run = await runner.FireAsync(auto.Id.Value);

        run.Outcome.ShouldBe("running");
        run.TaskId.ShouldBe("task-1");
        await delegation.Received(1).CreateTaskAsync(
            Arg.Is<CreateDelegationTaskRequest>(r =>
                r.Scope == $"automation:{auto.Id.Value}"
                && r.CliName == "claude"
                && r.Kind == "automation"
                && r.UseWorktree == false),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_Fire_Quando_Conclui_Entao_AutomationReagenda()
    {
        await using var ctx = NewContext();
        var auto = await SeedAutomation(ctx, "worktree:" + _workspace);
        var delegation = Substitute.For<IDelegationService>();
        delegation.CreateTaskAsync(Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DelegationTaskDto(
                "task-1", "s", "p", "claude", [], null, null, false, null,
                _workspace, null, null,
                Taskboard.Delegation.DelegationTaskStatus.Ready,
                null, null, DateTime.UtcNow, null, null, null));
        var runner = NewRunner(ctx, delegation);

        await runner.FireAsync(auto.Id.Value);

        var reloaded = await new EfCoreRepository<AgentAutomation>(NewContext())
            .GetAsync(auto.Id);
        reloaded.ShouldNotBeNull();
        reloaded.LastFiredAtUtc.ShouldNotBeNull();
        // cron → reagendado no futuro
        reloaded.NextFireAtUtc.ShouldBeGreaterThan(DateTime.UtcNow);
    }
}
