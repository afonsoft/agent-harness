using Taskboard.Application.Contracts.Jobs;

namespace Taskboard.Tests.Unit.Jobs;

/// <summary>Fake IJobScheduleStore em memória para testes do JobRegistry.</summary>
public sealed class InMemoryJobScheduleStore : IJobScheduleStore
{
    private readonly List<JobScheduleOverride> _rows = [];

    public int Upserts { get; private set; }

    public Task<IReadOnlyList<JobScheduleOverride>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<JobScheduleOverride>>(_rows.ToList());

    public Task UpsertAsync(JobScheduleOverride schedule, CancellationToken cancellationToken = default)
    {
        Upserts++;
        var existing = _rows.FindIndex(r => r.JobKey == schedule.JobKey);
        if (existing < 0)
        {
            _rows.Add(schedule);
        }
        else
        {
            _rows[existing] = schedule;
        }

        return Task.CompletedTask;
    }
}
