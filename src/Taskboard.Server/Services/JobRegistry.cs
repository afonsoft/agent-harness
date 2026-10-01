using System.Threading.Channels;
using Taskboard.Application.Contracts.Jobs;

namespace Taskboard.Server.Services;

/// <summary>
/// Signals understood by a <see cref="ManagedJobService"/> loop.
/// </summary>
public enum JobSignal
{
    /// <summary>Re-read the effective schedule (override changed) without running.</summary>
    ScheduleChanged,

    /// <summary>Run once immediately (operator-triggered).</summary>
    RunRequested
}

/// <summary>
/// Singleton <see cref="IJobRegistry"/>: static <see cref="JobDefinition"/>
/// catalog (DI), persisted <see cref="JobSchedule"/> overrides (EF, loaded
/// once then kept in memory), runtime state, a ~50-entry log ring buffer per
/// job and the wake channel driving manual runs / schedule changes.
/// SPEC-20260929-jobs-dashboard RF-002/RF-003.
/// </summary>
public sealed class JobRegistry : IJobRegistry
{
    private const int LogCapacity = 50;

    private sealed class JobRuntime
    {
        public required JobDefinition Definition { get; init; }
        public Channel<JobSignal> Signals { get; } = Channel.CreateUnbounded<JobSignal>();

        /// <summary>Serializes read-merge-persist in <see cref="SetOverrideAsync"/> (B-06).</summary>
        public SemaphoreSlim UpdateGate { get; } = new(1, 1);
        public JobScheduleOverride? Override;
        public bool IsRunning;
        public DateTimeOffset? LastStartedAt;
        public DateTimeOffset? LastCompletedAt;
        public string? LastOutcome;
        public string? LastMessage;
        public long RunCount;
        public readonly Queue<JobLogEntry> Log = new();
    }

    private readonly Dictionary<string, JobRuntime> _jobs = new(StringComparer.Ordinal);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JobRegistry> _logger;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private bool _overridesLoaded;

    public JobRegistry(
        IEnumerable<JobDefinition> definitions,
        IServiceScopeFactory scopeFactory,
        ILogger<JobRegistry> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        foreach (var definition in definitions)
        {
            _jobs[definition.Key] = new JobRuntime { Definition = definition };
        }
    }

    /// <summary>Channel reader consumed by the job's ManagedJobService loop.</summary>
    public ChannelReader<JobSignal> GetSignalReader(string key) => GetOrCreate(key).Signals.Reader;

    private JobRuntime? TryGet(string key)
    {
        lock (_jobs)
        {
            return _jobs.GetValueOrDefault(key);
        }
    }

    private JobRuntime GetOrCreate(string key)
    {
        lock (_jobs)
        {
            if (!_jobs.TryGetValue(key, out var runtime))
            {
                // A hosted service without a registered definition still runs —
                // it just cannot be reconfigured (defaults: enabled, 60min).
                runtime = new JobRuntime
                {
                    Definition = new JobDefinition(key, key, string.Empty, 3600, 60)
                };
                _jobs[key] = runtime;
                _logger.LogWarning("Managed job '{Key}' has no registered JobDefinition — using defaults.", key);
            }

            return runtime;
        }
    }

    public IReadOnlyList<JobStatusDto> GetStatuses()
    {
        List<JobRuntime> snapshot;
        lock (_jobs)
        {
            snapshot = _jobs.Values.ToList();
        }

        return snapshot
            .OrderBy(r => r.Definition.Key, StringComparer.Ordinal)
            .Select(ToDto)
            .ToList();
    }

    public async Task<JobEffectiveSchedule> GetEffectiveAsync(
        string key, CancellationToken cancellationToken = default)
    {
        await EnsureOverridesLoadedAsync(cancellationToken).ConfigureAwait(false);
        var runtime = GetOrCreate(key);
        lock (runtime)
        {
            var def = runtime.Definition;
            var enabled = runtime.Override?.Enabled ?? def.EnabledByDefault;
            var interval = TimeSpan.FromSeconds(
                runtime.Override?.IntervalSeconds ?? def.DefaultIntervalSeconds);
            return new JobEffectiveSchedule(enabled, interval, def.RunOnce);
        }
    }

    public async Task<JobUpdateResult> SetOverrideAsync(
        string key, bool? enabled, int? intervalSeconds, CancellationToken cancellationToken = default)
    {
        await EnsureOverridesLoadedAsync(cancellationToken).ConfigureAwait(false);
        var runtime = TryGet(key);
        if (runtime is null)
        {
            return new JobUpdateResult(null, JobUpdateError.UnknownJob, $"Unknown job '{key}'.");
        }

        var def = runtime.Definition;

        if (intervalSeconds is not null &&
            (intervalSeconds.Value < def.MinIntervalSeconds || intervalSeconds.Value < 1))
        {
            return new JobUpdateResult(
                null,
                JobUpdateError.IntervalOutOfRange,
                $"Interval must be >= {def.MinIntervalSeconds}s for job '{key}'.");
        }

        // B-06: read-merge-persist must be serialized per job — otherwise two
        // concurrent PUTs read the same "current" and lose each other's fields.
        await runtime.UpdateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            JobScheduleOverride schedule;
            lock (runtime)
            {
                var current = runtime.Override;
                schedule = new JobScheduleOverride(
                    key,
                    // B-05: keep the job definition's default — a bare "enable
                    // field omitted" PUT must not re-enable an off-by-default job.
                    enabled ?? current?.Enabled ?? def.EnabledByDefault,
                    intervalSeconds ?? current?.IntervalSeconds,
                    DateTime.UtcNow);
            }

            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<IJobScheduleStore>();
                await store.UpsertAsync(schedule, cancellationToken).ConfigureAwait(false);
            }

            lock (runtime)
            {
                runtime.Override = schedule;
            }
        }
        finally
        {
            runtime.UpdateGate.Release();
        }

        runtime.Signals.Writer.TryWrite(JobSignal.ScheduleChanged);
        return new JobUpdateResult(ToDto(runtime), JobUpdateError.None);
    }

    public JobTriggerResult Trigger(string key)
    {
        var runtime = TryGet(key);
        if (runtime is null)
        {
            return JobTriggerResult.UnknownJob;
        }

        lock (runtime)
        {
            if (runtime.IsRunning)
            {
                return JobTriggerResult.AlreadyRunning;
            }
        }

        runtime.Signals.Writer.TryWrite(JobSignal.RunRequested);
        return JobTriggerResult.Started;
    }

    public bool TryBeginRun(string key)
    {
        var runtime = GetOrCreate(key);
        lock (runtime)
        {
            if (runtime.IsRunning)
            {
                return false;
            }

            runtime.IsRunning = true;
            runtime.LastStartedAt = DateTimeOffset.UtcNow;
            runtime.RunCount++;
            AppendLog(runtime, "started", "run started");
            return true;
        }
    }

    public void ReportFinished(string key, bool success, string? message)
    {
        var runtime = GetOrCreate(key);
        lock (runtime)
        {
            runtime.IsRunning = false;
            runtime.LastCompletedAt = DateTimeOffset.UtcNow;
            runtime.LastOutcome = success ? "ok" : "error";
            runtime.LastMessage = message;
            AppendLog(runtime, runtime.LastOutcome, message ?? (success ? "completed" : "failed"));
        }
    }

    public void SignalScheduleChanged(string key)
    {
        GetOrCreate(key).Signals.Writer.TryWrite(JobSignal.ScheduleChanged);
    }

    private async Task EnsureOverridesLoadedAsync(CancellationToken cancellationToken)
    {
        if (_overridesLoaded)
        {
            return;
        }

        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_overridesLoaded)
            {
                return;
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IJobScheduleStore>();
            var rows = await store.ListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var row in rows)
            {
                var runtime = GetOrCreate(row.JobKey);
                lock (runtime)
                {
                    runtime.Override = row;
                }
            }

            _overridesLoaded = true;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private static void AppendLog(JobRuntime runtime, string outcome, string message)
    {
        runtime.Log.Enqueue(new JobLogEntry(DateTimeOffset.UtcNow, outcome, message));
        while (runtime.Log.Count > LogCapacity)
        {
            runtime.Log.Dequeue();
        }
    }

    private static JobStatusDto ToDto(JobRuntime runtime)
    {
        lock (runtime)
        {
            var def = runtime.Definition;
            return new JobStatusDto(
                def.Key,
                def.DisplayName,
                def.Description,
                runtime.Override?.Enabled ?? def.EnabledByDefault,
                runtime.Override?.IntervalSeconds ?? def.DefaultIntervalSeconds,
                def.DefaultIntervalSeconds,
                def.RunOnce,
                runtime.IsRunning,
                runtime.LastStartedAt,
                runtime.LastCompletedAt,
                runtime.LastOutcome,
                runtime.LastMessage,
                runtime.RunCount,
                runtime.Log.ToList());
        }
    }
}
