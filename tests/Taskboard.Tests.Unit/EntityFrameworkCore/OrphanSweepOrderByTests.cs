using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using Taskboard.EntityFrameworkCore.Data;
using Xunit;

namespace Taskboard.Tests.Unit.EntityFrameworkCore;

/// <summary>
/// Regression for issue #579 (EF Core 10102): the orphan-attachment sweep
/// queries used by <c>ChatRunRetentionService</c> and
/// <c>ChatService.SweepOrphanedAttachmentsAsync</c> take 500 rows — they must
/// carry a deterministic <c>OrderBy</c> or EF logs "row limiting operator
/// without OrderBy" once per boot. The warning is escalated to a throw via
/// <see cref="WarningsConfigurationBuilder"/> so the test fails if the
/// OrderBy is ever removed.
/// </summary>
public sealed class OrphanSweepOrderByTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TaskboardDbContext _context;

    public OrphanSweepOrderByTests()
    {
        _dbPath = Path.Join(Path.GetTempPath(), $"tb-sweep-{Guid.NewGuid()}.sqlite");
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .ConfigureWarnings(w => w.Throw(CoreEventId.RowLimitingOperationWithoutOrderByWarning))
            .Options;
        _context = new TaskboardDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public async Task Dado_OrphanSweep_Quando_Executa_Entao_OrderByDeterministicoSemWarning()
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);

        // Mesma forma da query em ChatRunRetentionService /
        // SweepOrphanedAttachmentsAsync — com o warning 10102 elevado a erro,
        // executar sem exceção prova o OrderBy.
        var orphans = await _context.ChatAttachments
            .Where(a => a.MessageId == null && a.CreatedAt < cutoff)
            .OrderBy(a => a.CreatedAt)
            .Take(500)
            .ToListAsync();

        orphans.ShouldBeEmpty();
        _context.ChatAttachments
            .Where(a => a.MessageId == null && a.CreatedAt < cutoff)
            .OrderBy(a => a.CreatedAt)
            .Take(500)
            .ToQueryString()
            .ShouldContain("ORDER BY");
    }
}
