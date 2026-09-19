using System.Collections.Concurrent;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Server.Services;

namespace Taskboard.Server.Services;

/// <summary>
/// Gerenciador de permissões inline para ferramentas de agentes em sessão interativa (ACP).
/// </summary>
public sealed class PermissionGate
{
    private sealed record PendingEntry(
        string RequestId,
        string ThreadId,
        string Tool,
        string Detail,
        IReadOnlyList<string> Options,
        DateTime CreatedAt,
        TaskCompletionSource<string> Tcs);

    private readonly ConcurrentDictionary<string, PendingEntry> _pending = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _alwaysAllowed = new();
    private readonly IThreadEventStreamService _eventStream;

    public PermissionGate(IThreadEventStreamService eventStream)
    {
        _eventStream = eventStream;
    }

    public async Task<string> RequestPermissionAsync(
        string threadId,
        string tool,
        string detail,
        IReadOnlyList<string> options,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        // Se já houver consentimento permanente (always) para a ferramenta nesta sessão:
        if (_alwaysAllowed.TryGetValue(threadId, out var set))
        {
            lock (set)
            {
                if (set.Contains(tool))
                {
                    return "allow";
                }
            }
        }

        var requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entry = new PendingEntry(requestId, threadId, tool, detail, options, DateTime.UtcNow, tcs);

        _pending[requestId] = entry;

        // Publica evento de permissão via SSE
        await _eventStream.PublishAsync(
            threadId,
            new ServerSentEvent(
                "ai_chat.permission",
                new PermissionRequestInfo(requestId, tool, detail, options)),
            ct).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromMinutes(5));

        using var reg = cts.Token.Register(() =>
        {
            if (_pending.TryRemove(requestId, out var expired))
            {
                expired.Tcs.TrySetResult("deny");
            }
        });

        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    public bool Reply(string threadId, string requestId, string outcome)
    {
        if (!_pending.TryRemove(requestId, out var entry))
        {
            return false;
        }

        if (entry.ThreadId != threadId)
        {
            return false;
        }

        var normalizedOutcome = outcome.ToLowerInvariant();
        if (normalizedOutcome == "always")
        {
            var set = _alwaysAllowed.GetOrAdd(threadId, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            lock (set)
            {
                set.Add(entry.Tool);
            }
            normalizedOutcome = "allow";
        }

        return entry.Tcs.TrySetResult(normalizedOutcome);
    }
}
