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
    string Kind = "task");

/// <summary>Wire shape of a mailbox message.</summary>
public sealed record MailboxMessageDto(
    string Id,
    string Scope,
    string FromAgent,
    string ToAgent,
    string Kind,
    string Payload,
    DateTime CreatedAt,
    DateTime? ReadAt);

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
    string? Kind = null);

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
    string Kind = AgentMailboxKinds.Text);

/// <summary>SPEC-20261007 RF-001: human reply to a mailbox message (body required; from defaults to "human").</summary>
public sealed record MailboxReplyRequest(string? Body, string? From);
