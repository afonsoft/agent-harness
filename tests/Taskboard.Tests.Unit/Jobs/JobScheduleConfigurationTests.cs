using Microsoft.EntityFrameworkCore;
using Shouldly;
using Taskboard.Domain.Entities;
using Taskboard.EntityFrameworkCore.Data;
using Xunit;

namespace Taskboard.Tests.Unit.Jobs;

/// <summary>SPEC-20260929-jobs-dashboard RF-003 — entidade JobSchedule + config EF.</summary>
public sealed class JobScheduleConfigurationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<TaskboardDbContext> _options;

    public JobScheduleConfigurationTests()
    {
        _dbPath = Path.Join(Path.GetTempPath(), $"tb-jobs-{Guid.NewGuid()}.sqlite");
        _options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        using var context = new TaskboardDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Dado_Modelo_Quando_Inspeciona_Entao_JobKeyEhUnicoEObrigatorio()
    {
        using var db = new TaskboardDbContext(_options);
        var entity = db.Model.FindEntityType(typeof(JobSchedule));

        entity.ShouldNotBeNull();
        entity.GetTableName().ShouldBe("JobSchedules");
        var jobKey = entity.GetProperty(nameof(JobSchedule.JobKey));
        jobKey.IsNullable.ShouldBeFalse();
        jobKey.GetMaxLength().ShouldBe(128);
        entity.GetIndexes().ShouldContain(i => i.IsUnique && i.Properties.Single().Name == nameof(JobSchedule.JobKey));
    }

    [Fact]
    public async Task Dado_Override_Quando_Persiste_Entao_RoundtripPreservaValores()
    {
        await using (var db = new TaskboardDbContext(_options))
        {
            db.JobSchedules.Add(new JobSchedule(Guid.NewGuid())
            {
                JobKey = "cli-probe-refresh",
                Enabled = false,
                IntervalSeconds = 300,
                UpdatedAt = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc)
            });
            await db.SaveChangesAsync();
        }

        await using (var db = new TaskboardDbContext(_options))
        {
            var row = await db.JobSchedules.SingleAsync();
            row.JobKey.ShouldBe("cli-probe-refresh");
            row.Enabled.ShouldBeFalse();
            row.IntervalSeconds.ShouldBe(300);
        }
    }

    [Fact]
    public async Task Dado_DuasLinhasMesmoJobKey_Quando_Save_Entao_ViolacaoDeUnicidade()
    {
        await using var db = new TaskboardDbContext(_options);
        db.JobSchedules.Add(new JobSchedule(Guid.NewGuid()) { JobKey = "job-a", UpdatedAt = DateTime.UtcNow });
        db.JobSchedules.Add(new JobSchedule(Guid.NewGuid()) { JobKey = "job-a", UpdatedAt = DateTime.UtcNow });

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
