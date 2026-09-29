using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Taskboard.Application.Contracts.Jobs;
using Taskboard.Domain.Entities;
using Taskboard.EntityFrameworkCore.Data;

namespace Taskboard.EntityFrameworkCore.Repositories;

/// <summary>EF Core persistence for job schedule overrides (JobSchedule table).</summary>
public sealed class EfCoreJobScheduleStore : IJobScheduleStore
{
    private readonly TaskboardDbContext _db;

    public EfCoreJobScheduleStore(TaskboardDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<JobScheduleOverride>> ListAsync(CancellationToken cancellationToken = default) =>
        await _db.JobSchedules.AsNoTracking()
            .Select(x => new JobScheduleOverride(x.JobKey, x.Enabled, x.IntervalSeconds, x.UpdatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task UpsertAsync(JobScheduleOverride schedule, CancellationToken cancellationToken = default)
    {
        var existing = await _db.JobSchedules
            .FirstOrDefaultAsync(x => x.JobKey == schedule.JobKey, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            _db.JobSchedules.Add(new JobSchedule(Guid.NewGuid())
            {
                JobKey = schedule.JobKey,
                Enabled = schedule.Enabled,
                IntervalSeconds = schedule.IntervalSeconds,
                UpdatedAt = schedule.UpdatedAt
            });
        }
        else
        {
            existing.Enabled = schedule.Enabled;
            existing.IntervalSeconds = schedule.IntervalSeconds;
            existing.UpdatedAt = schedule.UpdatedAt;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
