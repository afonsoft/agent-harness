using Taskboard;
using Taskboard.ValueObjects;

namespace Taskboard.Domain.Entities.Chat;

/// <summary>
/// A detached shell command started by <c>shell_exec run_in_background</c>
/// (SPEC-20261005-chat-jobs-schedule-search RF-001). The row outlives the
/// spawning run — output streams to <see cref="OutputPath"/> (a file under
/// the data dir) and completion writes a system note into the transcript so
/// the next turn sees the outcome.
/// </summary>
public sealed class ChatJob : Entity<ChatJobId>
{
    public const int MaxCommandLength = 2000;

    public ChatConversationId ConversationId { get; private set; } = default!; // NOSONAR S8970 — EF entity pattern usado em todo o Domain
    public ChatRunId? RunId { get; private set; }
    public string Command { get; private set; } = default!; // NOSONAR S8970 — EF entity pattern usado em todo o Domain
    public ChatJobStatus Status { get; private set; } = ChatJobStatus.Queued;
    public int? ExitCode { get; private set; }
    public int? Pid { get; private set; }
    public string? OutputPath { get; private set; }
    public string? Error { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? FinishedAt { get; private set; }

    private ChatJob()
    {
    }

    private ChatJob(
        ChatJobId id, ChatConversationId conversationId, ChatRunId? runId,
        string command, DateTime createdAt)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Job command cannot be empty.");
        }

        if (command.Length > MaxCommandLength)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Job command exceeds {MaxCommandLength} chars.");
        }

        ConversationId = conversationId;
        RunId = runId;
        Command = command.Trim();
        CreatedAt = createdAt;
    }

    public static ChatJob Create(
        ChatJobId id, ChatConversationId conversationId, ChatRunId? runId,
        string command, DateTime? now = null) =>
        new(id, conversationId, runId, command, now ?? DateTime.UtcNow);

    /// <summary>Registers the live process — pid + output capture path.</summary>
    public void MarkRunning(int pid, string outputPath, DateTime? now = null)
    {
        if (!Status.IsActive)
        {
            return;
        }

        Status = ChatJobStatus.Running;
        Pid = pid;
        OutputPath = outputPath;
        StartedAt = now ?? DateTime.UtcNow;
    }

    /// <summary>Natural exit — persists the code the process returned.</summary>
    public void Finish(int exitCode, DateTime? now = null) =>
        Settle(ChatJobStatus.Finished, exitCode, null, now);

    /// <summary>Operator kill (SIGTERM→SIGKILL) — exit code reported by the reap.</summary>
    public void Kill(int? exitCode, DateTime? now = null) =>
        Settle(ChatJobStatus.Killed, exitCode, null, now);

    /// <summary>Crash of the supervising task — the job never produced a code.</summary>
    public void Fail(string error, DateTime? now = null) =>
        Settle(ChatJobStatus.Terminated, null, error, now);

    /// <summary>Boot sweep: the host died mid-run — the OS process is orphaned.</summary>
    public void Terminate(string reason, DateTime? now = null) =>
        Settle(ChatJobStatus.Terminated, null, reason, now);

    private void Settle(ChatJobStatus status, int? exitCode, string? error, DateTime? now)
    {
        if (!status.IsTerminal || Status.IsTerminal)
        {
            return;
        }

        Status = status;
        ExitCode = exitCode ?? ExitCode;
        Error = error ?? Error;
        FinishedAt = now ?? DateTime.UtcNow;
        Pid = null;
    }
}
