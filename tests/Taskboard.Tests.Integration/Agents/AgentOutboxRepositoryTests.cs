using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.EntityFrameworkCore.Delegation;

namespace Taskboard.Tests.Integration.Agents;

/// <summary>
/// SPEC-20261104-agent-lineage-notes RF-005: the durable outbox index —
/// enqueue/list/trim-with-protected-kinds/flush/remove on real SQLite.
/// </summary>
public class AgentOutboxRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TaskboardDbContext _context;
    private readonly EfCoreAgentOutboxRepository _repository;

    public AgentOutboxRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new TaskboardDbContext(options);
        _context.Database.EnsureCreated();
        _repository = new EfCoreAgentOutboxRepository(_context);
    }

    [Fact]
    public async Task Dado_Entradas_Quando_ListPending_Entao_OrdemCronologicaPorSessao()
    {
        var now = DateTime.UtcNow;
        await _repository.EnqueueAsync("term:a", "m2", "note", now.AddSeconds(2));
        await _repository.EnqueueAsync("term:a", "m1", "text", now.AddSeconds(1));
        await _repository.EnqueueAsync("term:b", "m9", "note", now);

        var pending = await _repository.ListPendingAsync("term:a");

        pending.Count.ShouldBe(2);
        pending[0].MessageId.ShouldBe("m1");
        pending[1].MessageId.ShouldBe("m2");
        (await _repository.CountBySessionAsync("term:a")).ShouldBe(2);
        (await _repository.CountBySessionAsync("term:b")).ShouldBe(1);
    }

    [Fact]
    public async Task Dado_Overflow_Quando_Trim_Entao_DescartaAntigasMasProtegeKinds()
    {
        var now = DateTime.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            await _repository.EnqueueAsync("term:a", $"m{i}", "text", now.AddSeconds(i));
        }

        await _repository.EnqueueAsync("term:a", "keep-note", "note", now);
        await _repository.EnqueueAsync("term:a", "keep-done", "worker_done", now);

        var dropped = await _repository.TrimToCapacityAsync(
            "term:a", capacity: 3, ["note", "worker_done"]);

        dropped.ShouldBe(4); // 7 − cap 3; só os 5 "text" são droppáveis
        var pending = await _repository.ListPendingAsync("term:a");
        pending.Count.ShouldBe(3);
        pending.Select(e => e.MessageId).ShouldBe(["m4", "keep-note", "keep-done"],
            ignoreOrder: true);
    }

    [Fact]
    public async Task Dado_Entradas_Quando_Flush_Entao_LimpaSessao()
    {
        var now = DateTime.UtcNow;
        await _repository.EnqueueAsync("term:a", "m1", "note", now);
        await _repository.EnqueueAsync("term:b", "m2", "note", now);

        var flushed = await _repository.FlushAsync("term:a");

        flushed.ShouldBe(1);
        (await _repository.CountBySessionAsync("term:a")).ShouldBe(0);
        (await _repository.CountBySessionAsync("term:b")).ShouldBe(1);
    }

    [Fact]
    public async Task Dado_Entradas_Quando_RemoveIds_Entao_RemoveSoAsListadas()
    {
        var now = DateTime.UtcNow;
        var a = await _repository.EnqueueAsync("term:a", "m1", "note", now);
        var b = await _repository.EnqueueAsync("term:a", "m2", "note", now);

        var removed = await _repository.RemoveAsync([a.Id]);

        removed.ShouldBe(1);
        var pending = await _repository.ListPendingAsync("term:a");
        pending.ShouldHaveSingleItem().Id.ShouldBe(b.Id);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
