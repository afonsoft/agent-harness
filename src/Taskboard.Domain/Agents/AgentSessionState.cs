using Taskboard.Agents;

namespace Taskboard.Domain.Agents;

/// <summary>
/// SPEC-20261020-agent-attention-model RF-001: persisted current attention
/// state of one agent session (delegated task, live run or terminal session).
/// <see cref="SessionKey"/> is "<c>kind:id</c>" — e.g. <c>task:task-abc</c>,
/// <c>run:&lt;guid&gt;</c>, <c>thread:&lt;id&gt;</c>.
/// </summary>
public sealed class AgentSessionState : Entity<string>
{
    private AgentSessionState(string id)
        : base(id)
    {
    }

    public string SessionKey => Id;

    public string Cli { get; private set; } = string.Empty;
    public AgentAttentionState State { get; private set; } = AgentAttentionState.Working;

    /// <summary>Why the state needs attention — e.g. the pending question kind.</summary>
    public string? AttentionReason { get; private set; }

    public DateTime StateChangedAtUtc { get; private set; }
    public DateTime LastSignalAtUtc { get; private set; }
    public AttentionSignalKind LastSignalKind { get; private set; }

    /// <summary>Short evidence of the last signal (question snippet, event name); ≤200c.</summary>
    public string? Evidence { get; private set; }

    /// <summary>Optimistic concurrency (SPEC convention).</summary>
    public long Version { get; private set; }

    /// <summary>Count of consecutive weak signals observed — hysteresis input.</summary>
    public int WeakSignalCount { get; private set; }

    public static AgentSessionState Create(
        string sessionKey, string cli, DateTime atUtc) =>
        new(sessionKey)
        {
            Cli = cli.Trim(),
            State = AgentAttentionState.Working,
            StateChangedAtUtc = atUtc,
            LastSignalAtUtc = atUtc,
            LastSignalKind = AttentionSignalKind.Lifecycle,
        };

    public void ApplyTransition(
        AgentAttentionState next, AttentionSignalKind signal, DateTime atUtc,
        string? reason, string? evidence)
    {
        if (State != next)
        {
            State = next;
            StateChangedAtUtc = atUtc;
            AttentionReason = reason;
            Version++;
        }

        LastSignalAtUtc = atUtc;
        LastSignalKind = signal;
        Evidence = evidence is { Length: > 200 } ? evidence[..200] : evidence;
    }

    public void SetWeakSignals(int count) => WeakSignalCount = count;

    public void ResetWeakSignals() => WeakSignalCount = 0;
}

/// <summary>RF-001: append-only transition ledger — feeds the activity feed/audit.</summary>
public sealed class AgentSessionStateTransition : Entity<long>
{
    private AgentSessionStateTransition()
    {
    }

    public string SessionKey { get; private set; } = string.Empty;
    public AgentAttentionState FromState { get; private set; }
    public AgentAttentionState ToState { get; private set; }
    public AttentionSignalKind SignalKind { get; private set; }
    public string? Reason { get; private set; }
    public DateTime AtUtc { get; private set; }

    public static AgentSessionStateTransition Create(
        string sessionKey, AgentAttentionState from, AgentAttentionState to,
        AttentionSignalKind signal, string? reason, DateTime atUtc) =>
        new()
        {
            SessionKey = sessionKey,
            FromState = from,
            ToState = to,
            SignalKind = signal,
            Reason = reason is { Length: > 200 } ? reason[..200] : reason,
            AtUtc = atUtc,
        };
}
