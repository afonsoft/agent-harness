using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.AiChat;

namespace Taskboard.Server.Services;

/// <summary>
/// SPEC-20261020-agent-attention-model RF-008: broadcasts committed attention
/// transitions over the shared SSE stream (<c>/api/events</c>) as
/// <c>agent.attention</c> — the same reader every surface subscribes to.
/// </summary>
public sealed class EventStreamAgentAttentionBroadcaster : IAgentAttentionBroadcaster
{
    public const string EventType = "agent.attention";

    private readonly IEventStreamService _stream;

    public EventStreamAgentAttentionBroadcaster(IEventStreamService stream)
    {
        _stream = stream;
    }

    public Task BroadcastAsync(AgentSessionStateDto state, CancellationToken ct = default) =>
        _stream.PublishAsync(new ServerSentEvent(EventType, state), ct);
}
