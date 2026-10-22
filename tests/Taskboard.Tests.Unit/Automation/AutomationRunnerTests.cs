using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
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
        IConfiguration? configuration = null,
        IAgentLaunchResolver? launchResolver = null,
        IAgentFeedService? feed = null) =>
        new(
            new EfCoreRepository<AgentAutomation>(ctx),
            new EfCoreRepository<AgentAutomationRun>(ctx),
            delegation ?? Substitute.For<IDelegationService>(),
            processes ?? Substitute.For<IProcessRunner>(),
            configuration ?? Config(),
            NullLogger<AutomationRunner>.Instance,
            isolation: isolation,
            launchResolver: launchResolver,
            feed: feed);

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
        string? precheck = null,
        bool reuseSession = false,
        string? extraArgs = null,
        string cli = "claude")
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "demo"));
        var auto = AgentAutomation.Create(
            AgentAutomationId.NewGuid(),
            new AgentAutomationSpec(
                "test", "do the thing", cli, target,
                AutomationScheduleKinds.Cron, "0 9 * * *", null,
                DateTime.UtcNow.AddHours(1), precheck, extraArgs, 60, reuseSession, null, "local"));
        var repo = new EfCoreRepository<AgentAutomation>(ctx);
        await repo.AddAsync(auto);
        await repo.SaveChangesAsync();
        return auto;
    }

    private static DelegationTaskDto TaskDto(AgentAutomation auto, string id = "task-1") =>
        new(id, $"automation:{auto.Id.Value}", auto.Prompt, auto.AgentCli,
            [], null, null, false, null, "wd", null, null,
            Taskboard.Delegation.DelegationTaskStatus.Ready,
            null, null, DateTime.UtcNow, null, null, null, Kind: "automation");

    private static WorktreeSessionDto SessionDto(
        string runId, string path, string repoPath, string status = "Active") =>
        new("wt-1", runId, path, "auto/task", status, repoPath, "main",
            null, false, DateTime.UtcNow, DateTime.UtcNow, 1);

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

    [Fact]
    public async Task Dado_ReuseSessionComSessaoViva_Quando_FireAsync_Entao_ReutilizaWorktree()
    {
        await using var ctx = NewContext();
        var repoPath = Path.Combine(_workspace, "demo");
        var wtPath = Path.Combine(_workspace, "demo-wt");
        Directory.CreateDirectory(wtPath);
        var auto = await SeedAutomation(ctx, "repo:demo", reuseSession: true);

        // Run anterior succeeded com sessão bound.
        var prior = AgentAutomationRun.Start(AgentAutomationRunId.NewGuid(), auto.Id, DateTime.UtcNow.AddHours(-1));
        prior.Bind("task-old", "sess-1");
        prior.Finish(true, DateTime.UtcNow.AddHours(-1));
        var runRepo = new EfCoreRepository<AgentAutomationRun>(ctx);
        await runRepo.AddAsync(prior);
        await runRepo.SaveChangesAsync();

        var delegation = Substitute.For<IDelegationService>();
        delegation.CreateTaskAsync(Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>())
            .Returns(TaskDto(auto));
        delegation.AttachWorktreeAsync(
                "task-1", "sess-1", Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(TaskDto(auto));

        var isolation = Substitute.For<IWorkspaceIsolationService>();
        isolation.GetAsync("sess-1", Arg.Any<CancellationToken>())
            .Returns(SessionDto("sess-1", wtPath, repoPath));

        var runner = NewRunner(ctx, delegation, isolation: isolation);
        var run = await runner.FireAsync(auto.Id.Value);

        run.Outcome.ShouldBe("running");
        run.SessionId.ShouldBe("sess-1");
        await delegation.Received(1).CreateTaskAsync(
            Arg.Is<CreateDelegationTaskRequest>(r =>
                r.WorkspacePath == wtPath
                && r.UseWorktree == false
                && r.RepositoryPath == repoPath),
            Arg.Any<CancellationToken>());
        // rebind — nunca provisiona um worktree novo
        await isolation.DidNotReceive().CreateWorktreeAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_ReuseSessionComSessaoMorta_Quando_FireAsync_Entao_ProvisionaNovo()
    {
        await using var ctx = NewContext();
        var repoPath = Path.Combine(_workspace, "demo");
        var auto = await SeedAutomation(ctx, "repo:demo", reuseSession: true);

        var prior = AgentAutomationRun.Start(AgentAutomationRunId.NewGuid(), auto.Id, DateTime.UtcNow.AddHours(-1));
        prior.Bind("task-old", "sess-gone");
        prior.Finish(true, DateTime.UtcNow.AddHours(-1));
        var runRepo = new EfCoreRepository<AgentAutomationRun>(ctx);
        await runRepo.AddAsync(prior);
        await runRepo.SaveChangesAsync();

        var delegation = Substitute.For<IDelegationService>();
        delegation.CreateTaskAsync(Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>())
            .Returns(TaskDto(auto));

        var newPath = Path.Combine(_workspace, "fresh-wt");
        Directory.CreateDirectory(newPath);
        var isolation = Substitute.For<IWorkspaceIsolationService>();
        isolation.GetAsync("sess-gone", Arg.Any<CancellationToken>())
            .Returns((WorktreeSessionDto?)null);
        isolation.CreateWorktreeAsync(
                Arg.Any<string>(), repoPath, Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(SessionDto("sess-2", newPath, repoPath));

        var runner = NewRunner(ctx, delegation, isolation: isolation);
        var run = await runner.FireAsync(auto.Id.Value);

        run.SessionId.ShouldBe("sess-2");
        await delegation.Received(1).CreateTaskAsync(
            Arg.Is<CreateDelegationTaskRequest>(r => r.UseWorktree == true),
            Arg.Any<CancellationToken>());
        await isolation.Received(1).CreateWorktreeAsync(
            Arg.Any<string>(), repoPath, Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_ExtraArgs_Quando_FireAsync_Entao_ModelEEffortVaoNaTask()
    {
        await using var ctx = NewContext();
        var auto = await SeedAutomation(
            ctx, "worktree:" + _workspace,
            extraArgs: "--model opus --effort high --add-dir " + _workspace);

        var delegation = Substitute.For<IDelegationService>();
        delegation.CreateTaskAsync(Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>())
            .Returns(TaskDto(auto));

        var launchResolver = Substitute.For<IAgentLaunchResolver>();
        launchResolver.ResolveLaunchAsync("claude", "opus", "high", Arg.Any<CancellationToken>())
            .Returns(new LaunchResolution("opus", "high", []));

        var runner = NewRunner(ctx, delegation, launchResolver: launchResolver);
        var run = await runner.FireAsync(auto.Id.Value);

        run.Outcome.ShouldBe("running");
        await delegation.Received(1).CreateTaskAsync(
            Arg.Is<CreateDelegationTaskRequest>(r =>
                r.Model == "opus"
                && r.Effort == "high"
                && r.ExtraArgs != null
                && r.ExtraArgs.SequenceEqual(new[] { "--add-dir", _workspace })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_ArgsInvalidosPersistidos_Quando_FireAsync_Entao_SkippedSemTask()
    {
        // Defesa em profundidade — o service valida no save, mas uma row
        // adulterada/manualmente inserida não deve chegar ao spawn.
        await using var ctx = NewContext();
        var auto = await SeedAutomation(ctx, "worktree:" + _workspace);
        var tracked = await new EfCoreRepository<AgentAutomation>(ctx).GetAsync(auto.Id);
        // bypass do save-time guard via Update com args proibidos não é
        // possível (service valida) — simulamos entidade direto? entidade também
        // valida; portanto testamos o ramo via arg com flag recusada no parse
        // que passou pela entidade quando ExtraAgentArgs era permitido:
        // coverage do ramo: inserir run skip vem do parser no fire.
        tracked!.Update(null, null, null, null, "--bogus-flag x", null, null);
        var repo = new EfCoreRepository<AgentAutomation>(ctx);
        await repo.UpdateAsync(tracked);
        await repo.SaveChangesAsync();

        var delegation = Substitute.For<IDelegationService>();
        var runner = NewRunner(ctx, delegation);
        var run = await runner.FireAsync(auto.Id.Value);

        run.Outcome.ShouldBe("skipped");
        run.SkipReason.ShouldNotBeNull().ShouldContain("invalid_extra_args");
        await delegation.DidNotReceive().CreateTaskAsync(
            Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_FireSucesso_Quando_FeedDisponivel_Entao_PublicaAutomationRun()
    {
        await using var ctx = NewContext();
        var auto = await SeedAutomation(ctx, "worktree:" + _workspace);
        var delegation = Substitute.For<IDelegationService>();
        delegation.CreateTaskAsync(Arg.Any<CreateDelegationTaskRequest>(), Arg.Any<CancellationToken>())
            .Returns(TaskDto(auto));
        var feed = Substitute.For<IAgentFeedService>();

        var runner = NewRunner(ctx, delegation, feed: feed);
        await runner.FireAsync(auto.Id.Value);

        await feed.Received(1).ProduceAsync(
            Arg.Is<FeedItemRequest>(r =>
                r.Kind == AgentFeedKind.AutomationRun
                && r.Title.Contains("fired")
                && r.NeedsAttention == false
                && r.UserId == "local"),
            Arg.Any<CancellationToken>());
    }
}
