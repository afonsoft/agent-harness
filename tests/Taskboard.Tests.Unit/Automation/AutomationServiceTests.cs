using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Application.Automation;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Domain.Entities.Automation;
using Taskboard.Dtos;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.EntityFrameworkCore.Repositories;
using Taskboard.Integrations.Execution;
using Taskboard.Repositories;
using Taskboard.Server.Automation;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.Automation;

/// <summary>SPEC-20261021-agent-automations: schedule resolution, validation,
/// CRUD, precheck/missed-window semantics and run history.</summary>
public sealed class AutomationServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"tb-auto-{Guid.NewGuid():N}.db");
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

    private static IConfiguration Config(Dictionary<string, string?>? extra = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(extra ?? []).Build();

    private AutomationService NewService(
        TaskboardDbContext ctx,
        IAutomationRunner? runner = null,
        IConfiguration? configuration = null) =>
        new(
            new EfCoreRepository<AgentAutomation>(ctx),
            new EfCoreRepository<AgentAutomationRun>(ctx),
            runner ?? Substitute.For<IAutomationRunner>(),
            configuration ?? Config());

    private static CreateAutomationRequest ValidCreate(
        string schedule = "0 9 * * *",
        string target = "repo:demo",
        string? precheck = null,
        string? extraArgs = null,
        bool reuse = false) =>
        new("morning-triage", "Summarize open issues", "claude", target, schedule,
            TimeZoneId: null, MissedRunGraceMinutes: null, ReuseSession: reuse,
            ExtraAgentArgs: extraArgs, Precheck: precheck);

    [Fact]
    public async Task Dado_CreateCronValido_Quando_CreateAsync_Entao_PersisteComProximaFire()
    {
        await using var ctx = NewContext();
        var svc = NewService(ctx);

        var dto = await svc.CreateAsync(ValidCreate());

        dto.Id.ShouldNotBeNullOrEmpty();
        dto.ScheduleKind.ShouldBe("cron");
        dto.CronExpression.ShouldBe("0 9 * * *");
        dto.Enabled.ShouldBeTrue();
        dto.NextFireAtUtc.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(-1));
        dto.MissedRunGraceMinutes.ShouldBe(60);
        dto.Host.ShouldBe("local");
    }

    [Theory]
    [InlineData("hourly", "0 * * * *")]
    [InlineData("daily", "0 9 * * *")]
    [InlineData("weekdays", "0 9 * * 1-5")]
    [InlineData("weekly", "0 9 * * 1")]
    public async Task Dado_Preset_Quando_CreateAsync_Entao_ResolveParaCron(string preset, string expectedCron)
    {
        await using var ctx = NewContext();
        var svc = NewService(ctx);

        var dto = await svc.CreateAsync(ValidCreate(schedule: preset));

        dto.ScheduleKind.ShouldBe("cron");
        dto.CronExpression.ShouldBe(expectedCron);
    }

    [Fact]
    public async Task Dado_InstanteIso_Quando_CreateAsync_Entao_KindAt()
    {
        await using var ctx = NewContext();
        var svc = NewService(ctx);
        var at = DateTime.UtcNow.AddHours(2).ToString("o");

        var dto = await svc.CreateAsync(ValidCreate(schedule: at));

        dto.ScheduleKind.ShouldBe("at");
        dto.CronExpression.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_InstantePassado_Quando_CreateAsync_Entao_Rejeita()
    {
        await using var ctx = NewContext();
        var svc = NewService(ctx);
        var at = DateTime.UtcNow.AddHours(-1).ToString("o");

        await Should.ThrowAsync<DomainException>(
            () => svc.CreateAsync(ValidCreate(schedule: at)));
    }

    [Theory]
    [InlineData("0 9 * *")]           // 4 fields
    [InlineData("0 9 * * * *")]       // 6 fields
    [InlineData("99 * * * *")]        // out of range
    [InlineData("not a cron")]
    public async Task Dado_CronInvalido_Quando_CreateAsync_Entao_Rejeita(string cron)
    {
        await using var ctx = NewContext();
        var svc = NewService(ctx);

        await Should.ThrowAsync<DomainException>(
            () => svc.CreateAsync(ValidCreate(schedule: cron)));
    }

    [Theory]
    [InlineData("demo")]                 // sem prefixo
    [InlineData("other:demo")]           // prefixo desconhecido
    [InlineData("")]                     // vazio
    public async Task Dado_TargetInvalido_Quando_CreateAsync_Entao_Rejeita(string target)
    {
        await using var ctx = NewContext();
        var svc = NewService(ctx);

        await Should.ThrowAsync<DomainException>(
            () => svc.CreateAsync(ValidCreate(target: target)));
    }

    [Fact]
    public async Task Dado_ReuseSessionComExtraArgs_Quando_CreateAsync_Entao_Rejeita()
    {
        await using var ctx = NewContext();
        var svc = NewService(ctx);

        var ex = await Should.ThrowAsync<DomainException>(
            () => svc.CreateAsync(ValidCreate(reuse: true, extraArgs: "--model opus")));
        ex.Message.ShouldContain("ReuseSession");
    }

    [Fact]
    public async Task Dado_TimeZoneInvalida_Quando_CreateAsync_Entao_Rejeita()
    {
        await using var ctx = NewContext();
        var svc = NewService(ctx);

        await Should.ThrowAsync<DomainException>(
            () => svc.CreateAsync(ValidCreate() with { TimeZoneId = "Mars/Olympus" }));
    }

    [Fact]
    public async Task Dado_LimiteAtingido_Quando_CreateAsync_Entao_AutomationLimit()
    {
        await using var ctx = NewContext();
        var svc = NewService(ctx, configuration: Config(new()
        {
            ["Taskboard:Automations:MaxPerUser"] = "1",
        }));

        await svc.CreateAsync(ValidCreate());
        var ex = await Should.ThrowAsync<DomainException>(
            () => svc.CreateAsync(ValidCreate() with { Name = "second" }));
        ex.Message.ShouldContain("limit");
    }

    [Fact]
    public async Task Dado_Automation_Quando_UpdateAsync_Entao_AlteraCampos()
    {
        await using var ctx = NewContext();
        var svc = NewService(ctx);
        var dto = await svc.CreateAsync(ValidCreate());

        var updated = await svc.UpdateAsync(dto.Id, new UpdateAutomationRequest(
            Name: "nightly", Enabled: false, Schedule: "daily"));

        updated.ShouldNotBeNull();
        updated.Name.ShouldBe("nightly");
        updated.Enabled.ShouldBeFalse();
        updated.CronExpression.ShouldBe("0 9 * * *");
    }

    [Fact]
    public async Task Dado_Automation_Quando_RunNowAsync_Entao_DelegaAoRunner()
    {
        await using var ctx = NewContext();
        var runner = Substitute.For<IAutomationRunner>();
        var svc = NewService(ctx, runner: runner);
        var dto = await svc.CreateAsync(ValidCreate());
        runner.FireAsync(dto.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AgentAutomationRunDto(
                "run1", dto.Id, DateTime.UtcNow, "running", null, "task1", null, null, null, null)));

        var run = await svc.RunNowAsync(dto.Id);

        run.ShouldNotBeNull();
        run.Outcome.ShouldBe("running");
        await runner.Received(1).FireAsync(dto.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_RunNowInexistente_Quando_RunNowAsync_Entao_Null()
    {
        await using var ctx = NewContext();
        var svc = NewService(ctx);

        (await svc.RunNowAsync("missing")).ShouldBeNull();
    }

    [Fact]
    public async Task Dado_RunGravada_Quando_ListRunsAsync_Entao_Historico()
    {
        await using var ctx = NewContext();
        var runner = new FakeRunner(NewContext);
        var svc = NewService(ctx, runner: runner);
        var dto = await svc.CreateAsync(ValidCreate());

        await svc.RunNowAsync(dto.Id);
        var runs = await svc.ListRunsAsync(dto.Id);

        runs.Count.ShouldBe(1);
        runs[0].Outcome.ShouldBe("succeeded");
        runs[0].AutomationId.ShouldBe(dto.Id);
    }

    /// <summary>Runner fake que grava uma run succeeded no mesmo arquivo SQLite.</summary>
    private sealed class FakeRunner : IAutomationRunner
    {
        private readonly Func<TaskboardDbContext> _contextFactory;

        public FakeRunner(Func<TaskboardDbContext> contextFactory) => _contextFactory = contextFactory;

        public async Task<AgentAutomationRunDto> FireAsync(string automationId, CancellationToken ct = default)
        {
            await using var ctx = _contextFactory();
            var run = AgentAutomationRun.Start(
                AgentAutomationRunId.NewGuid(), AgentAutomationId.From(automationId), DateTime.UtcNow);
            run.Finish(true, DateTime.UtcNow, exitCode: 0);
            var repo = new EfCoreRepository<AgentAutomationRun>(ctx);
            await repo.AddAsync(run, ct);
            await repo.SaveChangesAsync(ct);
            return new AgentAutomationRunDto(
                run.Id.Value, automationId, run.FiredAtUtc, run.Outcome,
                0, "task-x", null, null, 0, run.FinishedAtUtc);
        }
    }
}
