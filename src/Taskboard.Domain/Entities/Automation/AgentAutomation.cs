using Taskboard.ValueObjects;

namespace Taskboard.Domain.Entities.Automation;

/// <summary>Schedule kinds for <see cref="AgentAutomation"/> (RF-001).</summary>
public static class AutomationScheduleKinds
{
    /// <summary>Recurring 5-field cron expression.</summary>
    public const string Cron = "cron";

    /// <summary>One-shot at an explicit UTC instant.</summary>
    public const string At = "at";

    public static bool IsValid(string kind) => kind is Cron or At;
}

/// <summary>Run outcomes for <see cref="AgentAutomationRun"/> (RF-002).</summary>
public static class AutomationRunOutcomes
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Skipped = "skipped";

    public static bool IsValid(string outcome) =>
        outcome is Running or Succeeded or Failed or Skipped;
}

/// <summary>Creation payload for <see cref="AgentAutomation.Create"/> —
/// keeps the factory under the parameter-count gate (S107).</summary>
public sealed record AgentAutomationSpec(
    string Name,
    string Prompt,
    string AgentCli,
    string Target,
    string ScheduleKind,
    string? CronExpression,
    string? TimeZoneId,
    DateTime NextFireAtUtc,
    string? Precheck,
    string? ExtraAgentArgs,
    int MissedRunGraceMinutes,
    bool ReuseSession,
    string? Host,
    string? Autonomy,
    string? CreatedBy);

/// <summary>
/// A scheduled prompt aimed at an agent CLI (SPEC-20261021-agent-automations
/// RF-001): at <see cref="NextFireAtUtc"/> the AutomationDispatcher runs the
/// optional precheck and creates a delegation task (or injects into a live
/// session when <see cref="ReuseSession"/>) against <see cref="Target"/>.
/// Rows are durable — a missed fire within <see cref="MissedRunGraceMinutes"/>
/// is delivered at boot.
/// </summary>
public sealed class AgentAutomation : Entity<AgentAutomationId>
{
    public const int MaxNameLength = 120;
    public const int MaxPromptLength = 4000;
    public const int MaxCronLength = 128;
    public const int MaxTimeZoneLength = 64;
    public const int MaxTargetLength = 512;
    public const int MaxPrecheckLength = 500;
    public const int MaxExtraArgsLength = 2000;
    public const int MaxGraceMinutes = 1440;

    public string Name { get; private set; } = default!; // NOSONAR S8970 — EF entity pattern usado em todo o Domain
    public string Prompt { get; private set; } = default!; // NOSONAR S8970
    /// <summary>Agent CLI kind key (claude/codex/… — <see cref="Agents.AgentCliKind"/>).</summary>
    public string AgentCli { get; private set; } = default!; // NOSONAR S8970
    /// <summary><c>repo:{name}</c> | <c>worktree:{path}</c> | <c>project:{id}</c>.</summary>
    public string Target { get; private set; } = default!; // NOSONAR S8970
    public string ScheduleKind { get; private set; } = default!; // NOSONAR S8970
    public string? CronExpression { get; private set; }
    public string? TimeZoneId { get; private set; }
    public DateTime NextFireAtUtc { get; private set; }
    public bool Enabled { get; private set; }
    public int MissedRunGraceMinutes { get; private set; }
    public bool ReuseSession { get; private set; }
    public string? ExtraAgentArgs { get; private set; }
    public string? Precheck { get; private set; }
    /// <summary>Remote-host label — accepted now, validated <c>local</c> until remote-hosts ships.</summary>
    public string? Host { get; private set; }

    /// <summary>
    /// SPEC-20261022-cli-autonomy-profiles RF-005: requested permission
    /// envelope for every run (<c>plan|edit|full</c>); null = default.
    /// </summary>
    public string? Autonomy { get; private set; }
    public string? CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastFiredAtUtc { get; private set; }
    public long Version { get; set; }

    private AgentAutomation()
    {
    }

    private AgentAutomation(AgentAutomationId id, AgentAutomationSpec spec, DateTime createdAt)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(spec.Name) || spec.Name.Length > MaxNameLength)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Automation name is required and must fit in {MaxNameLength} chars.");
        }

        if (string.IsNullOrWhiteSpace(spec.Prompt) || spec.Prompt.Length > MaxPromptLength)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Automation prompt is required and must fit in {MaxPromptLength} chars.");
        }

        if (string.IsNullOrWhiteSpace(spec.AgentCli))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "AgentCli is required.");
        }

        if (string.IsNullOrWhiteSpace(spec.Target) || spec.Target.Length > MaxTargetLength)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Target is required and must fit in {MaxTargetLength} chars (repo:|worktree:|project:).");
        }

        if (!AutomationScheduleKinds.IsValid(spec.ScheduleKind))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Unknown schedule kind '{spec.ScheduleKind}'.");
        }

        if (spec.ScheduleKind == AutomationScheduleKinds.Cron
            && string.IsNullOrWhiteSpace(spec.CronExpression))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "Cron schedules require an expression.");
        }

        if (spec.MissedRunGraceMinutes < 0 || spec.MissedRunGraceMinutes > MaxGraceMinutes)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"MissedRunGraceMinutes must be 0–{MaxGraceMinutes}.");
        }

        // RF-005: ExtraAgentArgs is meaningless for a reused session (argv is
        // fixed at spawn) — reject the combination at save time.
        if (spec.ReuseSession && !string.IsNullOrWhiteSpace(spec.ExtraAgentArgs))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                "ExtraAgentArgs is incompatible with ReuseSession.");
        }

        if (spec.Precheck?.Length > MaxPrecheckLength)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Precheck exceeds {MaxPrecheckLength} chars.");
        }

        if (spec.Autonomy is not null
            && !Taskboard.Agents.AgentAutonomyNames.IsValidName(spec.Autonomy.Trim().ToLowerInvariant()))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                "Autonomy must be plan|edit|full.");
        }

        if (spec.ExtraAgentArgs?.Length > MaxExtraArgsLength)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"ExtraAgentArgs exceeds {MaxExtraArgsLength} chars.");
        }

        Name = spec.Name.Trim();
        Prompt = spec.Prompt.Trim();
        AgentCli = spec.AgentCli.Trim().ToLowerInvariant();
        Target = spec.Target.Trim();
        ScheduleKind = spec.ScheduleKind;
        CronExpression = spec.CronExpression;
        TimeZoneId = string.IsNullOrWhiteSpace(spec.TimeZoneId) ? null : spec.TimeZoneId.Trim();
        NextFireAtUtc = spec.NextFireAtUtc;
        Enabled = true;
        MissedRunGraceMinutes = spec.MissedRunGraceMinutes;
        ReuseSession = spec.ReuseSession;
        ExtraAgentArgs = spec.ExtraAgentArgs;
        Precheck = spec.Precheck;
        Host = string.IsNullOrWhiteSpace(spec.Host) ? "local" : spec.Host.Trim();
        Autonomy = string.IsNullOrWhiteSpace(spec.Autonomy)
            ? null
            : spec.Autonomy.Trim().ToLowerInvariant();
        CreatedBy = spec.CreatedBy;
        CreatedAt = createdAt;
    }

    public static AgentAutomation Create(
        AgentAutomationId id, AgentAutomationSpec spec, DateTime? now = null) =>
        new(id, spec, now ?? DateTime.UtcNow);

    /// <summary>Stamps a fire and schedules the next one — <c>at</c> disables itself.</summary>
    public void MarkFired(DateTime now, DateTime? nextFireAtUtc)
    {
        LastFiredAtUtc = now;
        if (ScheduleKind == AutomationScheduleKinds.Cron && nextFireAtUtc is { } next)
        {
            NextFireAtUtc = next;
        }
        else
        {
            Enabled = false;
        }
    }

    /// <summary>Recomputes the fire instant without firing (missed-window skip).</summary>
    public void Reschedule(DateTime nextFireAtUtc)
    {
        NextFireAtUtc = nextFireAtUtc;
    }

    /// <summary>Replaces the schedule definition (kind + expr + tz) — used by
    /// service updates when the requested schedule resolves to another kind.</summary>
    public void SetSchedule(string kind, string? cronExpression, string? timeZoneId)
    {
        if (!AutomationScheduleKinds.IsValid(kind))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, $"Unknown schedule kind '{kind}'.");
        }
        ScheduleKind = kind;
        CronExpression = cronExpression;
        TimeZoneId = timeZoneId;
    }

    /// <summary>Pause/resume without deleting the row.</summary>
    public void SetEnabled(bool enabled, DateTime? nextFireAtUtc = null)
    {
        Enabled = enabled;
        if (nextFireAtUtc is { } next)
        {
            NextFireAtUtc = next;
        }
    }

    /// <summary>Edits name/prompt/target/extras — applied to the next fire.</summary>
    public void Update(
        string? name, string? prompt, string? target,
        string? precheck, string? extraAgentArgs,
        int? missedRunGraceMinutes, bool? reuseSession, string? autonomy = null)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            if (name.Length > MaxNameLength)
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidValue,
                    $"Automation name exceeds {MaxNameLength} chars.");
            }
            Name = name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(prompt))
        {
            if (prompt.Length > MaxPromptLength)
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidValue,
                    $"Automation prompt exceeds {MaxPromptLength} chars.");
            }
            Prompt = prompt.Trim();
        }

        if (!string.IsNullOrWhiteSpace(target))
        {
            if (target.Length > MaxTargetLength)
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidValue,
                    $"Target exceeds {MaxTargetLength} chars.");
            }
            Target = target.Trim();
        }

        if (precheck is not null)
        {
            if (precheck.Length > MaxPrecheckLength)
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidValue,
                    $"Precheck exceeds {MaxPrecheckLength} chars.");
            }
            Precheck = string.IsNullOrWhiteSpace(precheck) ? null : precheck;
        }

        var newExtra = extraAgentArgs ?? ExtraAgentArgs;
        var newReuse = reuseSession ?? ReuseSession;
        if (newReuse && !string.IsNullOrWhiteSpace(newExtra))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                "ExtraAgentArgs is incompatible with ReuseSession.");
        }

        if (newExtra?.Length > MaxExtraArgsLength)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"ExtraAgentArgs exceeds {MaxExtraArgsLength} chars.");
        }
        if (extraAgentArgs is not null)
        {
            ExtraAgentArgs = string.IsNullOrWhiteSpace(extraAgentArgs) ? null : extraAgentArgs;
        }

        if (reuseSession is { } rs)
        {
            ReuseSession = rs;
        }

        if (autonomy is not null)
        {
            if (!Taskboard.Agents.AgentAutonomyNames.IsValidName(autonomy.Trim().ToLowerInvariant()))
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidValue,
                    "Autonomy must be plan|edit|full.");
            }
            Autonomy = string.IsNullOrWhiteSpace(autonomy)
                ? null
                : autonomy.Trim().ToLowerInvariant();
        }

        if (missedRunGraceMinutes is { } grace)
        {
            if (grace < 0 || grace > MaxGraceMinutes)
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidValue,
                    $"MissedRunGraceMinutes must be 0–{MaxGraceMinutes}.");
            }
            MissedRunGraceMinutes = grace;
        }
    }
}

/// <summary>
/// One fired automation run (RF-002): created <c>running</c> when the
/// dispatcher picks the fire, closed <c>succeeded</c>/<c>failed</c>/
/// <c>skipped</c> with optional task/session linkage and skip reason.
/// </summary>
public sealed class AgentAutomationRun : Entity<AgentAutomationRunId>
{
    public const int MaxSkipReasonLength = 500;

    public AgentAutomationId AutomationId { get; private set; } = default!; // NOSONAR S8970
    public DateTime FiredAtUtc { get; private set; }
    public string Outcome { get; private set; } = default!; // NOSONAR S8970
    public int? ExitCode { get; private set; }
    public string? TaskId { get; private set; }
    public string? SessionId { get; private set; }
    public string? SkipReason { get; private set; }
    public long? DurationMs { get; private set; }
    public DateTime? FinishedAtUtc { get; private set; }

    private AgentAutomationRun()
    {
    }

    private AgentAutomationRun(AgentAutomationRunId id, AgentAutomationId automationId, DateTime firedAtUtc)
        : base(id)
    {
        AutomationId = automationId;
        FiredAtUtc = firedAtUtc;
        Outcome = AutomationRunOutcomes.Running;
    }

    public static AgentAutomationRun Start(
        AgentAutomationRunId id, AgentAutomationId automationId, DateTime firedAtUtc) =>
        new(id, automationId, firedAtUtc);

    /// <summary>Skipped without ever launching (precheck fail / missed window).</summary>
    public static AgentAutomationRun Skipped(
        AgentAutomationRunId id, AgentAutomationId automationId, DateTime firedAtUtc, string reason)
    {
        var run = new AgentAutomationRun(id, automationId, firedAtUtc);
        run.Outcome = AutomationRunOutcomes.Skipped;
        run.SkipReason = reason.Length > MaxSkipReasonLength ? reason[..MaxSkipReasonLength] : reason;
        run.FinishedAtUtc = firedAtUtc;
        run.DurationMs = 0;
        return run;
    }

    /// <summary>Binds the launched delegation task/session ids.</summary>
    public void Bind(string? taskId, string? sessionId)
    {
        TaskId = taskId;
        SessionId = sessionId;
    }

    /// <summary>Closes the run terminal.</summary>
    public void Finish(bool succeeded, DateTime finishedAtUtc, int? exitCode = null, string? skipReason = null)
    {
        Outcome = succeeded ? AutomationRunOutcomes.Succeeded : AutomationRunOutcomes.Failed;
        ExitCode = exitCode;
        SkipReason = skipReason is { Length: > MaxSkipReasonLength } ? skipReason[..MaxSkipReasonLength] : skipReason;
        FinishedAtUtc = finishedAtUtc;
        DurationMs = Math.Max(0, (long)(finishedAtUtc - FiredAtUtc).TotalMilliseconds);
    }
}
