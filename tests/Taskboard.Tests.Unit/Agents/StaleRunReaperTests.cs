using Microsoft.EntityFrameworkCore;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Agents;
using Taskboard.Domain.Agents;
using Taskboard.EntityFrameworkCore.Agents;
using Taskboard.EntityFrameworkCore.Data;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>SPEC-20260920-harness-maintenance-jobs RF-001 — reaper de runs órfãos.</summary>
public sealed class StaleRunReaperTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TaskboardDbContext _context;
    private readonly EfCoreAgentRunRepository _repository;
    private readonly StaleRunReaper _reaper;

    public StaleRunReaperTests()
    {
        _dbPath = Path.Join(Path.GetTempPath(), $"tb-reaper-{Guid.NewGuid()}.sqlite");
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        _context = new TaskboardDbContext(options);
        _context.Database.EnsureCreated();
        _repository = new EfCoreAgentRunRepository(_context);
        _reaper = new StaleRunReaper(_repository);
    }

    public void Dispose()
    {
        _context.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private async Task<AgentRun> AddRunAsync(DateTimeOffset startedAt, bool running = true)
    {
        var run = new AgentRun(Guid.NewGuid(), "issue-1", AgentType.Codex, startedAt);
        if (running)
        {
            run.MarkRunning();
        }

        _context.AgentRuns.Add(run);
        await _context.SaveChangesAsync();
        return run;
    }

    [Fact]
    public async Task Dado_RunVelhoSemJobVivo_Quando_Reap_Entao_FinalizaComoFailed()
    {
        var run = await AddRunAsync(DateTimeOffset.UtcNow.AddHours(-2));

        var reaped = await _reaper.RunOnceAsync([], TimeSpan.FromMinutes(15));

        reaped.ShouldBe([run.Id]);
        var reloaded = await _context.AgentRuns.FindAsync(run.Id);
        reloaded!.State.ShouldBe(AgentRunState.Failed);
        reloaded.FinishedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Dado_RunVelhoComJobVivo_Quando_Reap_Entao_NaoToca()
    {
        var run = await AddRunAsync(DateTimeOffset.UtcNow.AddHours(-2));

        var reaped = await _reaper.RunOnceAsync([run.Id], TimeSpan.FromMinutes(15));

        reaped.ShouldBeEmpty();
        var reloaded = await _context.AgentRuns.FindAsync(run.Id);
        reloaded!.State.ShouldBe(AgentRunState.Running);
    }

    [Fact]
    public async Task Dado_RunRecenteSemJobVivo_Quando_Reap_Entao_NaoToca()
    {
        // Janela de pickup do canal: Queued/Running novo ainda pode ser
        // despachado — não é staleness.
        var run = await AddRunAsync(DateTimeOffset.UtcNow.AddMinutes(-2), running: false);

        var reaped = await _reaper.RunOnceAsync([], TimeSpan.FromMinutes(15));

        reaped.ShouldBeEmpty();
        var reloaded = await _context.AgentRuns.FindAsync(run.Id);
        reloaded!.State.ShouldBe(AgentRunState.Queued);
    }

    [Fact]
    public async Task Dado_RunFinalizado_Quando_Reap_Entao_NaoECandidato()
    {
        var run = await AddRunAsync(DateTimeOffset.UtcNow.AddHours(-3));
        run.MarkFinished(AgentRunState.Succeeded, DateTimeOffset.UtcNow.AddHours(-2));
        await _context.SaveChangesAsync();

        var reaped = await _reaper.RunOnceAsync([], TimeSpan.FromMinutes(15));

        reaped.ShouldBeEmpty();
        var reloaded = await _context.AgentRuns.FindAsync(run.Id);
        reloaded!.State.ShouldBe(AgentRunState.Succeeded);
    }
}
