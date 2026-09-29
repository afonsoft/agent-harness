using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Taskboard.Application.Contracts.Jobs;

/// <summary>Persisted schedule override for one managed job (DTO — ports return DTOs, never entities).</summary>
public sealed record JobScheduleOverride(string JobKey, bool Enabled, int? IntervalSeconds, DateTime UpdatedAt);

/// <summary>
/// Persistence port for job schedule overrides (SPEC-20260929-jobs-dashboard
/// RF-003). Scoped service — the singleton JobRegistry resolves it through
/// IServiceScopeFactory per call.
/// </summary>
public interface IJobScheduleStore
{
    Task<IReadOnlyList<JobScheduleOverride>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates the override for <paramref name="schedule"/>'s JobKey.</summary>
    Task UpsertAsync(JobScheduleOverride schedule, CancellationToken cancellationToken = default);
}
