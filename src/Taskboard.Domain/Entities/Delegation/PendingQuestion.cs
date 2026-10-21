using Taskboard.Delegation;

namespace Taskboard.Domain.Entities.Delegation;

/// <summary>
/// SPEC-20261020-v2 RF-004: a worker → coordinator blocking question backed by
/// a mailbox message. Stays Pending until answered (agent_reply) or the asker
/// times out; --resume re-blocks on the same question without duplicating.
/// </summary>
public sealed class PendingQuestion
{
    private PendingQuestion() { }

    public string Id { get; private set; } = default!;
    public string Scope { get; private set; } = default!;
    public string RunId { get; private set; } = default!;

    /// <summary>Mailbox message carrying the question payload.</summary>
    public string MessageId { get; private set; } = default!;

    /// <summary>Asker address (worker key / cli).</summary>
    public string FromAgent { get; private set; } = default!;
    public string Question { get; private set; } = default!;

    /// <summary>Serialized options array; null = free-form answer.</summary>
    public string? OptionsJson { get; private set; }
    public PendingQuestionStatus Status { get; private set; }
    public string? Answer { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public long Version { get; private set; }

    public static PendingQuestion Create(
        string scope, string runId, string messageId, string fromAgent,
        string question, string? optionsJson, DateTime? now = null)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "question is required");
        }

        if (string.IsNullOrWhiteSpace(messageId))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "messageId is required");
        }

        return new PendingQuestion
        {
            Id = $"q-{Guid.NewGuid():N}",
            Scope = scope.Trim(),
            RunId = runId.Trim(),
            MessageId = messageId.Trim(),
            FromAgent = fromAgent.Trim(),
            Question = question.Trim(),
            OptionsJson = optionsJson,
            Status = PendingQuestionStatus.Pending,
            CreatedAtUtc = now ?? DateTime.UtcNow,
        };
    }

    /// <summary>Coordinator answered — terminal.</summary>
    public PendingQuestion MarkAnswered(string answer, DateTime? now = null)
    {
        if (Status is not PendingQuestionStatus.Pending)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"question_not_pending: {Id} is {Status}");
        }

        if (string.IsNullOrWhiteSpace(answer))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "answer is required");
        }

        Status = PendingQuestionStatus.Answered;
        Answer = answer.Trim();
        ResolvedAtUtc = now ?? DateTime.UtcNow;
        Version++;
        return this;
    }

    /// <summary>Asker timed out waiting — resumable via agent_ask --resume.</summary>
    public PendingQuestion TimeOut(DateTime? now = null)
    {
        if (Status is not PendingQuestionStatus.Pending)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"question_not_pending: {Id} is {Status}");
        }

        Status = PendingQuestionStatus.TimedOut;
        ResolvedAtUtc = now ?? DateTime.UtcNow;
        Version++;
        return this;
    }

    /// <summary>Re-block on the same question — resume reopens a timed-out one.</summary>
    public PendingQuestion Resume()
    {
        if (Status is PendingQuestionStatus.Answered)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"question_not_pending: {Id} is {Status}");
        }

        Status = PendingQuestionStatus.Pending;
        ResolvedAtUtc = null;
        Version++;
        return this;
    }
}
