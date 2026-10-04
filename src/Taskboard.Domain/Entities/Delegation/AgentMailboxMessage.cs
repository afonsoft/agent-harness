using Taskboard;
using Taskboard.Delegation;

namespace Taskboard.Domain.Entities.Delegation;

/// <summary>
/// SPEC-20261005 RF-002: one message in the per-scope agent mailbox —
/// how delegated agents, the dispatcher and the assistant exchange signals
/// (<c>worker_done</c>, <c>heartbeat</c>, <c>escalation</c>) and plain text.
/// </summary>
public sealed class AgentMailboxMessage : AggregateRoot<string>
{
    private AgentMailboxMessage(string id)
        : base(id)
    {
    }

    /// <summary>Owning scope — conversation id, or "harness" for ambient posts.</summary>
    public string Scope { get; private set; } = string.Empty;
    public string FromAgent { get; private set; } = string.Empty;

    /// <summary>"@all", "@idle", a cli/def name, a task id or the scope itself.</summary>
    public string ToAgent { get; private set; } = string.Empty;
    public string Kind { get; private set; } = AgentMailboxKinds.Text;
    public string Payload { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
    public DateTime? ReadAt { get; private set; }

    public static AgentMailboxMessage Create(
        string scope, string fromAgent, string toAgent, string payload,
        string kind = AgentMailboxKinds.Text, DateTime? now = null)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "payload is required");
        }

        if (string.IsNullOrWhiteSpace(toAgent))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "toAgent is required");
        }

        return new AgentMailboxMessage($"msg-{Guid.NewGuid():N}")
        {
            Scope = string.IsNullOrWhiteSpace(scope) ? "harness" : scope.Trim(),
            FromAgent = string.IsNullOrWhiteSpace(fromAgent) ? "assistant" : fromAgent.Trim(),
            ToAgent = toAgent.Trim(),
            Kind = string.IsNullOrWhiteSpace(kind) ? AgentMailboxKinds.Text : kind.Trim(),
            Payload = payload,
            CreatedAt = now ?? DateTime.UtcNow,
        };
    }

    public void MarkRead(DateTime? now = null) => ReadAt ??= now ?? DateTime.UtcNow;
}
