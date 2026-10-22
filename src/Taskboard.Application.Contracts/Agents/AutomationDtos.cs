namespace Taskboard.Dtos;

/// <summary>Automation row for list/detail (SPEC-20261021-agent-automations RF-001).</summary>
public sealed record AgentAutomationDto(
    string Id,
    string Name,
    string AgentCli,
    string Target,
    string ScheduleKind,
    string? CronExpression,
    string? TimeZoneId,
    DateTime NextFireAtUtc,
    bool Enabled,
    int MissedRunGraceMinutes,
    bool ReuseSession,
    string? ExtraAgentArgs,
    string? Precheck,
    string Host,
    string? CreatedBy,
    DateTime CreatedAt,
    DateTime? LastFiredAtUtc,
    long Version);

/// <summary>Run history row (RF-002).</summary>
public sealed record AgentAutomationRunDto(
    string Id,
    string AutomationId,
    DateTime FiredAtUtc,
    string Outcome,
    int? ExitCode,
    string? TaskId,
    string? SessionId,
    string? SkipReason,
    long? DurationMs,
    DateTime? FinishedAtUtc);

/// <summary>Create payload (RF-006). <paramref name="Schedule"/> accepts a
/// 5-field cron, an ISO-8601 UTC instant, or a preset
/// (<c>hourly|daily|weekdays|weekly</c>) resolved to cron on save.</summary>
public sealed record CreateAutomationRequest(
    string Name,
    string Prompt,
    string AgentCli,
    string Target,
    string Schedule,
    string? TimeZoneId = null,
    int? MissedRunGraceMinutes = null,
    bool ReuseSession = false,
    string? ExtraAgentArgs = null,
    string? Precheck = null,
    string? Host = null);

/// <summary>Patch update — null members keep their value.</summary>
public sealed record UpdateAutomationRequest(
    string? Name = null,
    string? Prompt = null,
    string? Target = null,
    string? Schedule = null,
    string? TimeZoneId = null,
    int? MissedRunGraceMinutes = null,
    bool? ReuseSession = null,
    string? ExtraAgentArgs = null,
    string? Precheck = null,
    bool? Enabled = null);
