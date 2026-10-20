using System.Collections.Concurrent;
using System.Security.Cryptography;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Application.Agents;

/// <summary>
/// SPEC-20261020-agent-attention-model RF-003: in-memory per-session hook
/// tokens. Opaque 256-bit tokens, rotated per session, never logged —
/// sessions are in-process anyway so persistence adds no value.
/// </summary>
public sealed class AgentHookTokenService : IAgentHookTokenService
{
    private readonly ConcurrentDictionary<string, string> _sessionByToken = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _tokenBySession = new(StringComparer.Ordinal);

    public string Issue(string sessionKey)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        if (_tokenBySession.TryRemove(sessionKey, out var old))
        {
            _sessionByToken.TryRemove(old, out _);
        }
        _tokenBySession[sessionKey] = token;
        _sessionByToken[token] = sessionKey;
        return token;
    }

    public string? Validate(string token) =>
        !string.IsNullOrEmpty(token)
        && _sessionByToken.TryGetValue(token, out var sessionKey)
            ? sessionKey : null;

    public void Revoke(string sessionKey)
    {
        if (_tokenBySession.TryRemove(sessionKey, out var token))
        {
            _sessionByToken.TryRemove(token, out _);
        }
    }
}
