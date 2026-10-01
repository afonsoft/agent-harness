using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Configuration;
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

    /// <summary>Orphaned sessions (connection dropped) are reaped after this grace period.</summary>
    internal static readonly TimeSpan OrphanTimeout = TimeSpan.FromMinutes(10);

    internal sealed class SessionEntry
    {
        public required IPtySession Session { get; init; }
        public required string UserKey { get; init; }
        /// <summary>Owning connection — null while orphaned between disconnect and reattach.</summary>
        public string? ConnectionId { get; set; }
        public required Func<string, string, Task> OnOutput { get; set; }
        public required Func<string, string, Task> OnClosed { get; set; }
        public DateTimeOffset? OrphanedAtUtc { get; set; }
        /// <summary>Bounded output scrollback replayed on reattach (SPEC-20260928 RF-003).</summary>
        public StringBuilder Scrollback { get; } = new();
    }

    /// <summary>
    /// Default scrollback cap per session — oldest output is dropped beyond it.
    /// SPEC-20261001-terminal-memory-mobile RF-002: 64 KB bounds memory even
    /// when eight sessions stay alive; overridable via
    /// <c>Terminal:ScrollbackChars</c>.
    /// </summary>
    internal const int DefaultScrollbackChars = 64_000;

    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new();
    private readonly Func<string?, IReadOnlyList<string>?, IPtySession> _sessionFactory;
    private readonly ILogger<TerminalSessionManager> _logger;
    private readonly TimeSpan _idleTimeout;
    private readonly TimeSpan _orphanTimeout;
    private readonly int _scrollbackLimit;
    private readonly CancellationTokenSource _sweepCts = new();
    private readonly Task _sweepTask;
    private readonly object _gate = new();

    public TerminalSessionManager(PtySessionFactory sessionFactory, ILogger<TerminalSessionManager> logger, IConfiguration? configuration = null)
        : this((workdir, command) => sessionFactory.Create(workdir, command: command), logger,
            scrollbackChars: ParseScrollbackChars(configuration))
    {
    }

    private static int? ParseScrollbackChars(IConfiguration? configuration) =>
        int.TryParse(configuration?["Terminal:ScrollbackChars"], out var chars) && chars > 0
            ? chars
            : null;

    internal TerminalSessionManager(
        Func<string?, IReadOnlyList<string>?, IPtySession> sessionFactory,
        ILogger<TerminalSessionManager> logger,
        TimeSpan? idleTimeout = null,
        TimeSpan? sweepInterval = null,
        TimeSpan? orphanTimeout = null,
        int? scrollbackChars = null)
    {
        _sessionFactory = sessionFactory;
        _logger = logger;
        _idleTimeout = idleTimeout ?? IdleTimeout;
        _orphanTimeout = orphanTimeout ?? OrphanTimeout;
        _scrollbackLimit = scrollbackChars is > 0 ? scrollbackChars.Value : DefaultScrollbackChars;
        _sweepTask = Task.Run(() => SweepLoopAsync(sweepInterval ?? TimeSpan.FromMinutes(1), _sweepCts.Token), _sweepCts.Token);
    }

    /// <summary>
    /// Creates and starts a new PTY session for <paramref name="userKey"/>.
    /// <paramref name="onOutput"/>/<paramref name="onClosed"/> receive the
    /// sessionId plus the payload so the caller can route per-tab.
    /// </summary>
    /// <param name="workdir">Optional cwd for the new session (SPEC-20260920
    /// RF-006 — resolved server-side, confined to the workspace root).</param>
    /// <param name="command">Arbitrary argv for the PTY (SPEC-20260928 —
    /// agent CLIs / docker exec); null → login bash.</param>
    /// <param name="requestedSessionId">Deterministic session id (e.g.
    /// "t-&lt;threadId&gt;" for AI Code terminal threads); when a live session
    /// already owns it, the open becomes a rebind — same id back. Max 64
    /// chars, [a-zA-Z0-9:_-].</param>
    /// <exception cref="InvalidOperationException">Session cap reached or the PTY failed to start.</exception>
    public Task<string> OpenAsync(
        string userKey,
        string connectionId,
        Func<string, string, Task> onOutput,
        Func<string, string, Task> onClosed,
        string? workdir = null,
        IReadOnlyList<string>? command = null,
        string? requestedSessionId = null)
    {
        lock (_gate)
        {
            var sessionId = NormalizeSessionId(requestedSessionId)
                ?? Guid.NewGuid().ToString("N")[..8];

            // Deterministic id already live → rebind to this connection and
            // return the same session (idempotent open for thread tabs). The
            // buffered scrollback replays through the new output delegate so a
            // fresh xterm shows the backlog (SPEC-20260928-ai-code-generic-cli
            // RF-003 — browser refresh must not lose visible output).
            // Rebind requires ownership: a session owned by another user must
            // never attach to this connection — it would leak output and
            // accept foreign input (SPEC-20260929-pty-session-security RF-001).
            if (_sessions.TryGetValue(sessionId, out var bound))
            {
                if (bound.Session.IsRunning)
                {
                    if (bound.UserKey != userKey)
                    {
                        throw new InvalidOperationException(
                            "Terminal session id is already in use.");
                    }

                    bound.ConnectionId = connectionId;
                    bound.OrphanedAtUtc = null;
                    bound.OnOutput = onOutput;
                    bound.OnClosed = onClosed;
                    var replay = bound.Scrollback.ToString();
                    if (replay.Length > 0)
                    {
                        _ = bound.OnOutput(sessionId, replay);
                    }

                    return Task.FromResult(sessionId);
                }

                // Stale entry (exited, Exited dispatch pending) — evict so the
                // deterministic id can be reused by a fresh session.
                if (_sessions.TryRemove(sessionId, out var stale))
                {
                    _ = DisposeEntryAsync(stale);
                }
            }

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

            var session = _sessionFactory(workdir, command);
            var entry = new SessionEntry
            {
                Session = session,
                UserKey = userKey,
                ConnectionId = connectionId,
                OnOutput = onOutput,
                OnClosed = onClosed
            };

            session.OutputReceived += chunk =>
            {
                AppendScrollback(entry, chunk);
                _ = entry.OnOutput(sessionId, chunk);
            };
            session.Exited += code => { _ = NotifyClosedAsync(sessionId, "exited"); };

            session.Start();
            _sessions[sessionId] = entry;

            _logger.LogInformation(
                "Terminal session {SessionId} started for {User} (connection {ConnectionId}).",
                sessionId, userKey, connectionId);
            return Task.FromResult(sessionId);
        }
    }

    private static string? NormalizeSessionId(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested) || requested.Length > 64)
        {
            return null;
        }

        if (requested.Any(c => !char.IsLetterOrDigit(c) && c is not '-' and not '_' and not ':'))
        {
            return null;
        }

        return requested;
    }

    private void AppendScrollback(SessionEntry entry, string chunk)
    {
        // Mutations share _gate with every reader (rebind replay, GetScrollback)
        // so a concurrent reattach never sees a torn buffer (RF-005).
        lock (_gate)
        {
            entry.Scrollback.Append(chunk);
            if (entry.Scrollback.Length > _scrollbackLimit)
            {
                entry.Scrollback.Remove(0, entry.Scrollback.Length - _scrollbackLimit);
            }
        }
    }

    /// <summary>Current buffered scrollback for a session — replay on reattach.</summary>
    public string GetScrollback(string sessionId)
    {
        lock (_gate)
        {
            return _sessions.TryGetValue(sessionId, out var entry)
                ? entry.Scrollback.ToString()
                : string.Empty;
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

    /// <summary>
    /// Marks every session of a dropped connection as orphaned — the PTY keeps
    /// running so a reconnected client can reattach within
    /// <see cref="OrphanTimeout"/> (SPEC-20260920-terminal-pty-resize RF-003).
    /// Orphans reject input until rebound.
    /// </summary>
    public Task OrphanAllForConnectionAsync(string connectionId)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _sessions)
        {
            if (pair.Value.ConnectionId == connectionId)
            {
                pair.Value.ConnectionId = null;
                pair.Value.OrphanedAtUtc = now;
                _logger.LogInformation(
                    "Terminal session {SessionId} orphaned (connection {ConnectionId} ended).",
                    pair.Key, connectionId);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Rebinds an orphaned (or still-owned) session to a new connection.
    /// Returns false when the session is gone — reaped, exited, or foreign.
    /// </summary>
    public Task<bool> ReattachAsync(
        string userKey,
        string connectionId,
        string sessionId,
        Func<string, string, Task> onOutput,
        Func<string, string, Task> onClosed)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var entry)
                || entry.UserKey != userKey
                || !entry.Session.IsRunning)
            {
                return Task.FromResult(false);
            }

            entry.ConnectionId = connectionId;
            entry.OrphanedAtUtc = null;
            entry.OnOutput = onOutput;
            entry.OnClosed = onClosed;
            _logger.LogInformation(
                "Terminal session {SessionId} reattached to connection {ConnectionId}.",
                sessionId, connectionId);
            return Task.FromResult(true);
        }
    }

    /// <summary>Closes sessions idle longer than the timeout or orphaned past the grace period.</summary>
    internal async Task SweepIdleAsync()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _sessions)
        {
            var orphanExpired = pair.Value.OrphanedAtUtc is { } orphanAt
                && now - orphanAt > _orphanTimeout;
            var idleExpired = now - pair.Value.Session.LastActivityUtc > _idleTimeout;
            if (!orphanExpired && !idleExpired)
            {
                continue;
            }

            try
            {
                if (_sessions.TryRemove(pair.Key, out var entry))
                {
                    var reason = orphanExpired ? "connection lost" : "idle-timeout";
                    _logger.LogInformation(
                        "Terminal session {SessionId} closed ({Reason}).", pair.Key, reason);
                    await NotifyAsync(entry, pair.Key, reason).ConfigureAwait(false);
                    await DisposeEntryAsync(entry).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // One bad entry must not abort the sweep of the remaining ones.
                _logger.LogWarning(ex, "Terminal sweep failed for session {SessionId}.", pair.Key);
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
            _logger.LogInformation(
                "Terminal session {SessionId} closed ({Reason}).", sessionId, reason);
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
                try
                {
                    await SweepIdleAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // The reaper must survive a bad tick — log and keep sweeping.
                    _logger.LogError(ex, "Terminal sweep tick failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the expected shutdown path — nothing to clean up.
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
            // Best-effort — failure here is non-fatal.
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
