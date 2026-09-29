using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Taskboard.Application.Contracts.Jobs;

/// <summary>
/// Static catalog entry describing one managed background job
/// (SPEC-20260929-jobs-dashboard RF-002). Registered in DI; persisted
/// overrides (JobSchedule row) win over these defaults.
/// </summary>
public sealed record JobDefinition(
    string Key,
    string DisplayName,
    string Description,
    int DefaultIntervalSeconds,
    int MinIntervalSeconds,
    bool RunOnce = false,
    bool EnabledByDefault = true);

/// <summary>A single entry of the per-job in-memory ring buffer log.</summary>
public sealed record JobLogEntry(DateTimeOffset Timestamp, string Outcome, string Message);

/// <summary>Runtime view of a managed job for <c>GET /api/jobs</c>.</summary>
public sealed record JobStatusDto(
    string Key,
    string Name,
    string Description,
    bool Enabled,
    int IntervalSeconds,
    int DefaultIntervalSeconds,
    bool RunOnce,
    bool IsRunning,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastCompletedAt,
    string? LastOutcome,
    string? LastMessage,
    long RunCount,
    IReadOnlyList<JobLogEntry> Log);

/// <summary><c>PUT /api/jobs/{key}</c> payload — null fields keep current value.</summary>
public sealed record UpdateJobRequest(bool? Enabled, int? IntervalSeconds);

/// <summary>Effective schedule for one job: persisted override ?? definition default.</summary>
public sealed record JobEffectiveSchedule(bool Enabled, TimeSpan Interval, bool RunOnce);

public enum JobUpdateError
{
    None,
    UnknownJob,
    IntervalOutOfRange
}

public sealed record JobUpdateResult(JobStatusDto? Status, JobUpdateError Error, string? Message = null);

public enum JobTriggerResult
{
    Started,
    AlreadyRunning,
    UnknownJob
}

/// <summary>
/// Singleton registry of managed background jobs: static definitions,
/// persisted schedule overrides, runtime state, per-job log ring buffer and
/// the wake channel used for manual runs / schedule changes.
/// </summary>
public interface IJobRegistry
{
    IReadOnlyList<JobStatusDto> GetStatuses();

    /// <summary>Effective schedule (DB override ?? definition default).</summary>
    Task<JobEffectiveSchedule> GetEffectiveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Persists the override and wakes the job loop — no restart.</summary>
    Task<JobUpdateResult> SetOverrideAsync(
        string key, bool? enabled, int? intervalSeconds, CancellationToken cancellationToken = default);

    /// <summary>Requests an immediate run; single-flight with the periodic tick.</summary>
    JobTriggerResult Trigger(string key);

    /// <summary>Called by ManagedJobService — returns false when a run is already in flight.</summary>
    bool TryBeginRun(string key);

    void ReportFinished(string key, bool success, string? message);

    /// <summary>Marks the schedule as changed so the loop re-evaluates without waiting a full interval.</summary>
    void SignalScheduleChanged(string key);
}
