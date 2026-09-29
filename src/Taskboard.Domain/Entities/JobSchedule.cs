using System;

namespace Taskboard.Domain.Entities;

/// <summary>
/// Persisted schedule override for a managed background job
/// (SPEC-20260929-jobs-dashboard RF-003). When present, Enabled and
/// IntervalSeconds win over the job definition defaults — applied without
/// restart because the job loop re-reads the effective schedule each tick.
/// </summary>
public sealed class JobSchedule : Entity<Guid>
{
    /// <summary>Stable job identifier (e.g. <c>cli-probe-refresh</c>).</summary>
    public string JobKey { get; set; } = string.Empty;

    /// <summary>When false the hosted service stays alive but skips every tick.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Interval override in seconds; null = definition default.</summary>
    public int? IntervalSeconds { get; set; }

    public DateTime UpdatedAt { get; set; }

    public JobSchedule(Guid id)
        : base(id)
    {
    }
}
