using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Taskboard.Integrations.Terminal;

/// <summary>
/// Registry of <see cref="IPtySession"/>s keyed by (user, sessionId) so a user
/// can run several terminal tabs over a single SignalR connection
/// (SPEC-20260917-terminal-tabs RF-001). Output/close notifications are routed
/// back through per-session delegates supplied by the caller (the hub), so the
/// manager stays transport-agnostic and unit-testable.
/// </summary>
public sealed class TerminalSessionManager : IAsyncDisposable
{
    /// <summary>Maximum concurrent sessions per user.</summary>
    internal const int MaxSessionsPerUser = 8;

    /// <summary>Session is closed after this much time without input.</summary>
    internal static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    internal sealed record SessionEntry(
        IPtySession Session,
        string UserKey,
        string ConnectionId,
        Func<string, string, Task> OnOutput,
        Func<string, string, Task> OnClosed);

    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new();
    private readonly Func<IPtySession> _sessionFactory;
    private readonly ILogger<TerminalSessionManager> _logger;
    private readonly TimeSpan _idleTimeout;
    private readonly CancellationTokenSource _sweepCts = new();
    private readonly Task _sweepTask;
    private readonly object _gate = new();

    public TerminalSessionManager(PtySessionFactory sessionFactory, ILogger<TerminalSessionManager> logger)
        : this(() => sessionFactory.Create(), logger)
    {
    }

    internal TerminalSessionManager(
        Func<IPtySession> sessionFactory,
        ILogger<TerminalSessionManager> logger,
        TimeSpan? idleTimeout = null,
        TimeSpan? sweepInterval = null)
    {
        _sessionFactory = sessionFactory;
        _logger = logger;
        _idleTimeout = idleTimeout ?? IdleTimeout;
        _sweepTask = Task.Run(() => SweepLoopAsync(sweepInterval ?? TimeSpan.FromMinutes(1), _sweepCts.Token));
    }

    /// <summary>
    /// Creates and starts a new PTY session for <paramref name="userKey"/>.
    /// <paramref name="onOutput"/>/<paramref name="onClosed"/> receive the
    /// sessionId plus the payload so the caller can route per-tab.
    /// </summary>
    /// <exception cref="InvalidOperationException">Session cap reached or the PTY failed to start.</exception>
    public Task<string> OpenAsync(
        string userKey,
        string connectionId,
        Func<string, string, Task> onOutput,
        Func<string, string, Task> onClosed)
    {
        lock (_gate)
        {
            var count = 0;
            foreach (var existing in _sessions.Values)
            {
                if (existing.UserKey == userKey)
                {
                    count++;
                }
            }

            if (count >= MaxSessionsPerUser)
            {
                throw new InvalidOperationException(
                    $"Maximum of {MaxSessionsPerUser} terminal sessions per user.");
            }

            var sessionId = Guid.NewGuid().ToString("N")[..8];
            var session = _sessionFactory();
            var entry = new SessionEntry(session, userKey, connectionId, onOutput, onClosed);

            session.OutputReceived += chunk => { _ = entry.OnOutput(sessionId, chunk); };
            session.Exited += code => { _ = NotifyClosedAsync(sessionId, "exited"); };

            session.Start();
            _sessions[sessionId] = entry;

            _logger.LogInformation(
                "Terminal session {SessionId} started for {User} (connection {ConnectionId}).",
                sessionId, userKey, connectionId);
            return Task.FromResult(sessionId);
        }
    }

    /// <summary>Writes input to a session owned by this connection; unknown/foreign ids are ignored.</summary>
    public async Task InputAsync(string userKey, string connectionId, string sessionId, string data)
    {
        if (TryGetOwned(userKey, connectionId, sessionId, out var entry))
        {
            await entry.Session.WriteAsync(data).ConfigureAwait(false);
        }
    }

    /// <summary>Resizes a session owned by this connection; unknown/foreign ids are ignored.</summary>
    public async Task ResizeAsync(string userKey, string connectionId, string sessionId, int cols, int rows)
    {
        if (TryGetOwned(userKey, connectionId, sessionId, out var entry))
        {
            await entry.Session.ResizeAsync(cols, rows).ConfigureAwait(false);
        }
    }

    /// <summary>Closes a session owned by this connection; idempotent.</summary>
    public async Task CloseAsync(string userKey, string connectionId, string sessionId)
    {
        if (!TryGetOwned(userKey, connectionId, sessionId, out _))
        {
            return;
        }

        if (_sessions.TryRemove(sessionId, out var entry))
        {
            _logger.LogInformation("Terminal session {SessionId} closed by client.", sessionId);
            await DisposeEntryAsync(entry).ConfigureAwait(false);
        }
    }

    /// <summary>Disposes every session owned by a connection (disconnect/shutdown).</summary>
    public async Task CloseAllForConnectionAsync(string connectionId)
    {
        foreach (var pair in _sessions)
        {
            if (pair.Value.ConnectionId == connectionId && _sessions.TryRemove(pair.Key, out var entry))
            {
                _logger.LogInformation(
                    "Terminal session {SessionId} closed (connection {ConnectionId} ended).",
                    pair.Key, connectionId);
                await DisposeEntryAsync(entry).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Closes sessions idle longer than the timeout; invoked by the sweeper (and tests).</summary>
    internal async Task SweepIdleAsync()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _sessions)
        {
            if (now - pair.Value.Session.LastActivityUtc <= _idleTimeout)
            {
                continue;
            }

            if (_sessions.TryRemove(pair.Key, out var entry))
            {
                _logger.LogInformation(
                    "Terminal session {SessionId} closed after idle timeout.", pair.Key);
                await NotifyAsync(entry, pair.Key, "idle-timeout").ConfigureAwait(false);
                await DisposeEntryAsync(entry).ConfigureAwait(false);
            }
        }
    }

    private bool TryGetOwned(string userKey, string connectionId, string sessionId, out SessionEntry entry)
    {
        return _sessions.TryGetValue(sessionId, out entry!)
            && entry.UserKey == userKey
            && entry.ConnectionId == connectionId;
    }

    private async Task NotifyClosedAsync(string sessionId, string reason)
    {
        if (_sessions.TryRemove(sessionId, out var entry))
        {
            await NotifyAsync(entry, sessionId, reason).ConfigureAwait(false);
            await DisposeEntryAsync(entry).ConfigureAwait(false);
        }
    }

    private static async Task NotifyAsync(SessionEntry entry, string sessionId, string reason)
    {
        try
        {
            await entry.OnClosed(sessionId, reason).ConfigureAwait(false);
        }
        catch
        {
            // Client may be gone; closing still proceeds.
        }
    }

    private async Task SweepLoopAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await SweepIdleAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task DisposeEntryAsync(SessionEntry entry)
    {
        try
        {
            await entry.Session.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Disposal best-effort.
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _sweepCts.CancelAsync().ConfigureAwait(false);
            await _sweepTask.ConfigureAwait(false);
        }
        catch
        {
        }

        _sweepCts.Dispose();

        foreach (var pair in _sessions)
        {
            if (_sessions.TryRemove(pair.Key, out var entry))
            {
                await DisposeEntryAsync(entry).ConfigureAwait(false);
            }
        }
    }
}
