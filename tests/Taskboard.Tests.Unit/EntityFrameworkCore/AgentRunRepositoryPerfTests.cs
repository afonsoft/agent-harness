using Microsoft.EntityFrameworkCore;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Domain.Agents;
using Taskboard.EntityFrameworkCore.Agents;
using Taskboard.EntityFrameworkCore.Data;
using Xunit;

namespace Taskboard.Tests.Unit.EntityFrameworkCore;

/// <summary>
/// SPEC-20261003-perf-pass RF-002/RF-003: the latest-per-issue and stale-run
/// queries must translate to SQL on SQLite (a non-translatable WHERE throws)
/// and produce the same results as the previous implementations.
/// </summary>
public sealed class AgentRunRepositoryPerfTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TaskboardDbContext _context;
    private readonly EfCoreAgentRunRepository _repository;

    public AgentRunRepositoryPerfTests()
    {
        _dbPath = Path.Join(Path.GetTempPath(), $"tb-perf-{Guid.NewGuid()}.sqlite");
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        _context = new TaskboardDbContext(options);
        _context.Database.EnsureCreated();
        _repository = new EfCoreAgentRunRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private async Task<AgentRun> AddRunAsync(
        string issueId,
        DateTimeOffset startedAt,
        AgentRunState state = AgentRunState.Queued)
    {
        var run = new AgentRun(Guid.NewGuid(), issueId, AgentType.Codex, startedAt);
        switch (state)
        {
            case AgentRunState.Running:
                run.MarkRunning();
                break;
            case AgentRunState.Succeeded:
            case AgentRunState.Failed:
            case AgentRunState.Canceled:
                run.MarkRunning();
                run.MarkFinished(state, startedAt.AddMinutes(1));
                break;
        }

        _context.AgentRuns.Add(run);
        await _context.SaveChangesAsync();
        return run;
    }

    [Fact]
    public async Task Dado_RunsDeDuasIssues_Quando_GetLatestPerIssue_Entao_RetornaAMaisRecenteDeCada()
    {
        var now = DateTimeOffset.UtcNow;
        await AddRunAsync("issue-1", now.AddMinutes(-30), AgentRunState.Succeeded);
        var latest1 = await AddRunAsync("issue-1", now.AddMinutes(-5));
        await AddRunAsync("issue-1", now.AddMinutes(-20), AgentRunState.Failed);
        var latest2 = await AddRunAsync("issue-2", now.AddMinutes(-2), AgentRunState.Running);
        await AddRunAsync("issue-2", now.AddMinutes(-40), AgentRunState.Failed);

        var result = await _repository.GetLatestPerIssueAsync();

        result.Count.ShouldBe(2);
        result.Select(r => r.Id).ShouldBe([latest1.Id, latest2.Id], ignoreOrder: true);
        result.Single(r => r.IssueId == "issue-2").State.ShouldBe(AgentRunState.Running);
    }

    [Fact]
    public async Task Dado_RunsMisturadas_Quando_GetStaleActiveRuns_Entao_FiltraEstadoECutoffNoSql()
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddMinutes(-15);
        var staleQueued = await AddRunAsync("issue-1", now.AddMinutes(-30));
        var staleRunning = await AddRunAsync("issue-2", now.AddMinutes(-60), AgentRunState.Running);
        await AddRunAsync("issue-3", now.AddMinutes(-2)); // ativo mas recente
        await AddRunAsync("issue-4", now.AddMinutes(-90), AgentRunState.Succeeded); // velho mas finalizado

        var result = await _repository.GetStaleActiveRunsAsync(cutoff);

        // Se StartedAt < cutoff não traduzisse para SQL, o EF Core lançaria
        // "could not be translated" aqui — o teste executando já prova a tradução.
        result.Select(r => r.Id).ShouldBe([staleQueued.Id, staleRunning.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task Dado_CutoffFuturo_Quando_GetStaleActiveRuns_Entao_TodosOsAtivosVoltam()
    {
        var now = DateTimeOffset.UtcNow;
        var queued = await AddRunAsync("issue-1", now.AddMinutes(-5));
        var running = await AddRunAsync("issue-2", now.AddMinutes(-3), AgentRunState.Running);
        await AddRunAsync("issue-3", now.AddMinutes(-1), AgentRunState.Failed);

        var result = await _repository.GetStaleActiveRunsAsync(now.AddHours(1));

        result.Select(r => r.Id).ShouldBe([queued.Id, running.Id], ignoreOrder: true);
    }
}
