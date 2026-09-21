using Taskboard.Agents;

namespace Taskboard.Server.Services;

/// <summary>
/// No-op <see cref="IAgentExecutionEventSink"/> used when
/// <c>Taskboard:AgentEvents:Enabled</c> is false — the fast rollback path
/// promised by SPEC-20260921-agent-execution-event-pipeline. Events are
/// neither persisted nor broadcast; replay returns empty.
/// </summary>
public sealed class NullAgentExecutionEventSink : IAgentExecutionEventSink
{
    public Task<AgentExecutionEvent> EmitAsync(AgentExecutionEvent evt, CancellationToken cancellationToken = default)
        => Task.FromResult(evt);

    public Task<IReadOnlyList<AgentExecutionEvent>> GetEventsAsync(
        string scopeKind, string scopeId, long after = 0, int take = 500, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AgentExecutionEvent>>([]);
}
