using System.Collections.Concurrent;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Integrations.Agents.Attention;

/// <summary>
/// SPEC-20261104-agent-attention-manifests RF-007: last detection verdict per
/// session — backs <c>agent explain</c>. Session-keyed; dropped when the
/// session closes.
/// </summary>
public sealed class AgentDetectionExplainStore : IAgentDetectionExplainStore
{
    private readonly ConcurrentDictionary<string, AgentDetectionExplainDto> _bySession =
        new(StringComparer.Ordinal);

    public void Record(string sessionKey, AgentDetectionExplainDto explain) =>
        _bySession[sessionKey] = explain;

    public AgentDetectionExplainDto? Get(string sessionKey) =>
        _bySession.TryGetValue(sessionKey, out var explain) ? explain : null;

    public void Discard(string sessionKey) => _bySession.TryRemove(sessionKey, out _);
}
