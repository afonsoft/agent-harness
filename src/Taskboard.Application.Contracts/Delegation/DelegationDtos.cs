using Taskboard.Delegation;

namespace Taskboard.Dtos;

/// <summary>Wire shape of a delegation task (inspection endpoints + tools).</summary>
public sealed record DelegationTaskDto(
    string Id,
    string Scope,
    string Prompt,
    string CliName,
    IReadOnlyList<string> DependsOn,
    string? RetryOf,
    string? FanoutGroupId,
    bool UseWorktree,
    string? WorktreeRunId,
    string WorkspacePath,
    string? RepositoryPath,
    string? BaseCommitSha,
    DelegationTaskStatus Status,
    string? ResultSummary,
    string? Error,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    DateTime? LastHeartbeatAt,
    string Kind = "task",
    string? RunId = null,
    string? ActiveDispatchId = null);

/// <summary>SPEC-20261020-v2 RF-001: wire shape of a coordination run.</summary>
public sealed record DelegationRunDto(
    string Id,
    string Scope,
    string Objective,
    string CoordinatorKey,
    DelegationRunStatus Status,
    DateTime CreatedAtUtc,
    DateTime? SettledAtUtc);

/// <summary>SPEC-20261020-v2 RF-002: wire shape of a dispatch attempt.</summary>
public sealed record DispatchDto(
    string Id,
    string TaskId,
    string RunId,
    string WorkerKey,
    int Attempt,
    DispatchState State,
    string PreambleJson,
    string? TerminalHandle,
    string? ArchivedOutput,
    DateTime CreatedAtUtc,
    DateTime? DispatchedAtUtc,
    DateTime? SettledAtUtc,
    DateTime? ReleasedAtUtc);

/// <summary>SPEC-20261020-v2 RF-003: wire shape of a mailbox delivery batch.</summary>
public sealed record DeliveryDto(
    string Id,
    string RunId,
    IReadOnlyList<string> MessageIds,
    DateTime CreatedAtUtc,
    DateTime? AckedAtUtc);

/// <summary>SPEC-20261020-v2 RF-003: agent_check request — ack/peek/all/wait/types.</summary>
public sealed record CheckDeliveriesRequest(
    string Scope,
    IReadOnlyCollection<string> Recipients,
    string? AckDeliveryId = null,
    bool Peek = false,
    bool All = false,
    IReadOnlyCollection<string>? Types = null,
    bool Wait = false,
    TimeSpan? Timeout = null);

/// <summary>SPEC-20261020-v2 RF-003: one delivery page returned by agent_check.</summary>
public sealed record DeliveryCheckResult(
    DeliveryDto? Delivery,
    IReadOnlyList<MailboxMessageDto> Messages,
    int PendingCount = 0,
    IReadOnlyList<DeliveryDto>? Deliveries = null,
    bool TimedOut = false);

/// <summary>SPEC-20261020-v2 RF-001: delegate_run_show payload.</summary>
public sealed record DelegationRunShowDto(
    DelegationRunDto Run,
    IReadOnlyList<DelegationTaskDto> Tasks,
    IReadOnlyList<DispatchDto> OpenDispatches,
    IReadOnlyList<DeliveryDto> UnackedDeliveries);

/// <summary>SPEC-20261020-v2 RF-007: retry receipt — same task, attempt+1 dispatch.</summary>
public sealed record RetryTaskResult(DelegationTaskDto Task, DispatchDto Dispatch);

/// <summary>Wire shape of a mailbox message.</summary>
public sealed record MailboxMessageDto(
    string Id,
    string Scope,
    string FromAgent,
    string ToAgent,
    string Kind,
    string Payload,
    DateTime CreatedAt,
    DateTime? ReadAt,
    string? DeliveryId = null,
    string? DispatchId = null);

/// <summary>Create request for a delegation task (tool → service boundary).</summary>
public sealed record CreateDelegationTaskRequest(
    string Prompt,
    string CliName,
    string Scope,
    string WorkspacePath,
    IReadOnlyList<string>? DependsOn = null,
    string? RetryOf = null,
    string? FanoutGroupId = null,
    bool UseWorktree = false,
    string? RepositoryPath = null,
    string? BaseCommitSha = null,
    string? Kind = null,
    string? RunId = null);

/// <summary>SPEC-20261020-v2 RF-002: create request for a dispatch attempt.</summary>
public sealed record CreateDispatchRequest(
    string TaskId,
    string RunId,
    string WorkerKey,
    int Attempt,
    string PreambleJson,
    string? Id = null);

/// <summary>SPEC-20261009 RF-002: one row of the merged delegation activity feed.</summary>
public sealed record DelegationEventDto(
    DateTime Timestamp,
    string Kind,
    string Text,
    string? TaskId = null);

/// <summary>SPEC-20261009 RF-003: delegate a board issue into the DAG.</summary>
public sealed record DelegateIssueRequest(
    string RepositoryFullName,
    int IssueNumber,
    string Title,
    string? Body = null,
    string? Cli = null);

/// <summary>Create request for a mailbox message.</summary>
public sealed record PostMailboxMessageRequest(
    string Scope,
    string FromAgent,
    string ToAgent,
    string Payload,
    string Kind = AgentMailboxKinds.Text,
    string? TaskId = null,
    string? DispatchId = null);

/// <summary>SPEC-20261007 RF-001: human reply to a mailbox message (body required; from defaults to "human").</summary>
public sealed record MailboxReplyRequest(string? Body, string? From);
