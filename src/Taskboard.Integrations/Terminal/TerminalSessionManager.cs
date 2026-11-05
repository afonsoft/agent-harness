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
        /// <summary>cwd the PTY started in — a deterministic id rebound to a
        /// different workdir gets recycled so the shell lands in the right dir.</summary>
        public string? Workdir { get; init; }
        public DateTimeOffset? OrphanedAtUtc { get; set; }
        /// <summary>Bounded output scrollback replayed on reattach (SPEC-20260928 RF-003).</summary>
        /// <summary>Attention-model key — <c>term:{sessionId}</c>.</summary>
        public string? AttentionKey { get; init; }
        /// <summary>argv[0] of the spawned command — used by the sessions
        /// live-join (SPEC-20261020-session-history-parity RF-003).</summary>
        public string? Cli { get; init; }
        public StringBuilder Scrollback { get; } = new();

        /// <summary>
        /// SPEC-20261104-run-terminal-runtime: runtime-owned PTY (delegated
        /// run terminal) — exempt from the idle reaper while pinned; the
        /// runtime is the completion authority, not inactivity.
        /// </summary>
        public bool RuntimeOwned { get; set; }

        /// <summary>Extra output tap for the owning service (wait registry) — fire-and-forget.</summary>
        public Func<string, string, Task>? OnChunk { get; init; }

        /// <summary>
        /// Process-exit hook invoked before teardown — receives the session
        /// id, exit code and the buffered scrollback tail (for archival);
        /// the session is already out of the registry when it runs.
        /// </summary>
        public Func<string, int, string, Task>? OnExited { get; init; }

        /// <summary>First-person keystroke hook (RF-007 control-owner flip).</summary>
        public Func<string, Task>? OnHumanInput { get; init; }

        /// <summary>All viewers detached — the runtime regains exclusive control.</summary>
        public Func<string, Task>? OnViewersEmpty { get; init; }

        /// <summary>Read+type viewers attached to the session (human attach, RF-007).</summary>
        public List<ViewerEntry> Viewers { get; } = [];
    }

    /// <summary>A non-owning viewer bound to a connection (multi-viewer attach).</summary>
    internal sealed class ViewerEntry
    {
        public required string ConnectionId { get; init; }
        public required string UserKey { get; init; }
        public required Func<string, string, Task> OnOutput { get; set; }
        public required Func<string, string, Task> OnClosed { get; set; }
    }

    /// <summary>
    /// Default scrollback cap per session — oldest output is dropped beyond it.
    /// SPEC-20261001-terminal-memory-mobile RF-002: 64 KB bounds memory even
    /// when eight sessions stay alive; overridable via
    /// <c>Terminal:ScrollbackChars</c>.
    /// </summary>
    internal const int DefaultScrollbackChars = 64_000;

    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new();
    private readonly Func<string?, IReadOnlyList<string>?, IReadOnlyDictionary<string, string>?, IPtySession> _sessionFactory;
    private readonly ILogger<TerminalSessionManager> _logger;
    private readonly TimeSpan _idleTimeout;
    private readonly TimeSpan _orphanTimeout;
    private readonly int _scrollbackLimit;
    private readonly CancellationTokenSource _sweepCts = new();
    private readonly Task _sweepTask;
    private readonly object _gate = new();
    private readonly Taskboard.Application.Contracts.Agents.IAgentOutputAttentionSink? _outputSink;
    private readonly Taskboard.Application.Contracts.Agents.IAgentAttentionService? _attention;
    private readonly Execution.MemoryPressureGate? _pressureGate;

    public TerminalSessionManager(PtySessionFactory sessionFactory, ILogger<TerminalSessionManager> logger, IConfiguration? configuration = null, Taskboard.Application.Contracts.Agents.IAgentOutputAttentionSink? outputSink = null, Taskboard.Application.Contracts.Agents.IAgentAttentionService? attention = null, Execution.MemoryPressureGate? pressureGate = null)
        : this((workdir, command, env) => sessionFactory.Create(workdir, command: command, environment: env), logger,
            scrollbackChars: ParseScrollbackChars(configuration))
    {
        _outputSink = outputSink;
        _attention = attention;
        _pressureGate = pressureGate;
    }

    private static int? ParseScrollbackChars(IConfiguration? configuration) =>
        int.TryParse(configuration?["Terminal:ScrollbackChars"], out var chars) && chars > 0
            ? chars
            : null;

    internal TerminalSessionManager(
        Func<string?, IReadOnlyList<string>?, IReadOnlyDictionary<string, string>?, IPtySession> sessionFactory,
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
        // SPEC-20261020-agent-process-isolation RF-004: PSI gate — refuse new
        // sessions while the server cgroup is under memory pressure.
        // Rebinds to existing sessions are exempt: they create nothing.
        if (_pressureGate is not null && !_pressureGate.Allow(out _))
        {
            throw new InvalidOperationException(
                "MEMORY_PRESSURE: server under memory pressure; retry later.");
        }

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

                    if (SameWorkdir(bound.Workdir, workdir))
                    {
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

                    // A live session whose workdir no longer matches (e.g. the
                    // conversation's repo changed) gets recycled — the
                    // deterministic id is reused by the fresh PTY below so the
                    // shell lands in the requested directory.
                    if (_sessions.TryRemove(sessionId, out var staleRunning))
                    {
                        _ = DisposeEntryAsync(staleRunning);
                    }
                }
                else
                {
                    // Stale entry (exited, Exited dispatch pending) — evict so the
                    // deterministic id can be reused by a fresh session.
                    if (_sessions.TryRemove(sessionId, out var stale))
                    {
                        _ = DisposeEntryAsync(stale);
                    }
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

            var session = _sessionFactory(workdir, command, null);
            var entry = new SessionEntry
            {
                Session = session,
                UserKey = userKey,
                ConnectionId = connectionId,
                OnOutput = onOutput,
                OnClosed = onClosed,
                Workdir = workdir,
                AttentionKey = $"term:{sessionId}",
                Cli = command is { Count: > 0 } ? command[0] : null,
            };

            // SPEC-20261020-agent-attention-model RF-005/RF-006: tail-match
            // interactive CLI output (OSC titles + permission/question patterns).
            var cliName = entry.Cli;
            session.OutputReceived += chunk => FanOut(entry, sessionId, cliName, chunk);
            session.Exited += code => { _ = NotifyClosedAsync(sessionId, "exited", code); };

            session.Start();
            _sessions[sessionId] = entry;

            _logger.LogInformation(
                "Terminal session {SessionId} started for {User} (connection {ConnectionId}).",
                sessionId, userKey, connectionId);
            return Task.FromResult(sessionId);
        }
    }

    /// <summary>
    /// SPEC-20261020-session-history-parity RF-003: live PTY sessions as
    /// (attentionKey, cli, cwd) rows for the sessions-index live-join.
    /// </summary>
    public IReadOnlyList<Taskboard.Application.Contracts.Agents.LiveAgentSessionDto> LiveSnapshot()
    {
        var list = new List<Taskboard.Application.Contracts.Agents.LiveAgentSessionDto>(_sessions.Count);
        foreach (var (id, entry) in _sessions)
        {
            if (entry.Session.IsRunning)
            {
                list.Add(new($"term:{id}", entry.Cli, entry.Workdir));
            }
        }

        return list;
    }

    /// <summary>Workdir equality for rebind-vs-recycle — normalized full paths.</summary>
    private static bool SameWorkdir(string? a, string? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            comparison);
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

    /// <summary>
    /// Writes input to a session owned by this connection — or viewed by it
    /// (RF-007: viewer keystrokes flip the run terminal to human control via
    /// the <c>OnHumanInput</c> hook). Unknown/foreign ids are ignored.
    /// </summary>
    public async Task InputAsync(string userKey, string connectionId, string sessionId, string data)
    {
        if (TryGetOwned(userKey, connectionId, sessionId, out var entry))
        {
            await entry.Session.WriteAsync(data).ConfigureAwait(false);
            return;
        }

        ViewerEntry? viewer = null;
        SessionEntry? owned = null;
        lock (_gate)
        {
            if (_sessions.TryGetValue(sessionId, out var found))
            {
                owned = found;
                viewer = found.Viewers.FirstOrDefault(
                    v => v.ConnectionId == connectionId && v.UserKey == userKey);
            }
        }

        if (viewer is not null && owned is not null)
        {
            await owned.Session.WriteAsync(data).ConfigureAwait(false);
            if (owned.OnHumanInput is { } hook)
            {
                _ = hook(sessionId);
            }
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

    /// <summary>Whether a live session exists under <paramref name="sessionId"/>.</summary>
    public bool IsAlive(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var entry) && entry.Session.IsRunning;

    /// <summary>
    /// SPEC-20261020-agent-hibernation RF-002: kills a session regardless of the
    /// owning connection — graceful tree kill + PTY release via dispose.
    /// Returns false when nothing was registered under the id.
    /// </summary>
    public async Task<bool> KillAsync(string sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out var entry))
        {
            return false;
        }

        _logger.LogInformation(
            "Terminal session {SessionId} killed for hibernation.", sessionId);
        await NotifyAsync(entry, sessionId, "hibernated").ConfigureAwait(false);
        await DisposeEntryAsync(entry).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// SPEC-20261020-agent-hibernation RF-007: respawns a hibernated session
    /// detached — no owning connection; the tab reattaches on focus via
    /// <see cref="ReattachAsync"/>/<see cref="OpenAsync"/> (same id → same
    /// <c>term:{id}</c> attention key). Env markers ride the PTY process.
    /// Idempotent: an id already live is reused, never duplicated.
    /// </summary>
    public Task<bool> RespawnDetachedAsync(
        string userKey,
        string sessionId,
        string? workdir,
        IReadOnlyList<string> command,
        IReadOnlyDictionary<string, string>? environment = null) =>
        OpenDetachedAsync(userKey, sessionId, workdir, command, environment);

    /// <summary>
    /// SPEC-20261104-run-terminal-runtime: spawns a detached PTY with runtime
    /// hooks — <paramref name="runtimeOwned"/> pins the session against the
    /// idle reaper (the runtime is the lifecycle authority), and the hook
    /// delegates receive output taps, the exit code, first-person input and
    /// the viewers-empty signal.
    /// </summary>
    public Task<bool> OpenDetachedAsync(
        string userKey,
        string sessionId,
        string? workdir,
        IReadOnlyList<string> command,
        IReadOnlyDictionary<string, string>? environment = null,
        bool runtimeOwned = false,
        Func<string, string, Task>? onChunk = null,
        Func<string, int, string, Task>? onExited = null,
        Func<string, Task>? onHumanInput = null,
        Func<string, Task>? onViewersEmpty = null,
        string? attentionKey = null)
    {
        lock (_gate)
        {
            var id = NormalizeSessionId(sessionId);
            if (id is null)
            {
                return Task.FromResult(false);
            }

            if (_sessions.TryGetValue(id, out var bound) && bound.Session.IsRunning)
            {
                return Task.FromResult(bound.UserKey == userKey);
            }

            if (_sessions.TryRemove(id, out var stale))
            {
                _ = DisposeEntryAsync(stale);
            }

            // Runtime-owned sessions are orchestrator infrastructure — the
            // per-user tab cap does not apply to them.
            var count = 0;
            if (!runtimeOwned)
            {
                foreach (var existing in _sessions.Values)
                {
                    if (existing.UserKey == userKey)
                    {
                        count++;
                    }
                }
            }

            if (count >= MaxSessionsPerUser)
            {
                return Task.FromResult(false);
            }

            var session = _sessionFactory(workdir, command, environment);
            var entry = new SessionEntry
            {
                Session = session,
                UserKey = userKey,
                ConnectionId = null,
                OnOutput = (_, _) => Task.CompletedTask,
                OnClosed = (_, _) => Task.CompletedTask,
                Workdir = workdir,
                AttentionKey = attentionKey ?? $"term:{id}",
                Cli = command.Count > 0 ? command[0] : null,
                RuntimeOwned = runtimeOwned,
                OnChunk = onChunk,
                OnExited = onExited,
                OnHumanInput = onHumanInput,
                OnViewersEmpty = onViewersEmpty,
            };

            var cliName = entry.Cli;
            session.OutputReceived += chunk => FanOut(entry, id, cliName, chunk);
            session.Exited += code => { _ = NotifyClosedAsync(id, "exited", code); };

            session.Start();
            _sessions[id] = entry;
            _logger.LogInformation(
                "Terminal session {SessionId} spawned detached for {User} (runtime-owned: {RuntimeOwned}).",
                id, userKey, runtimeOwned);
            return Task.FromResult(true);
        }
    }

    /// <summary>Output fan-out: scrollback → owner → viewers → runtime tap → attention sink.</summary>
    private void FanOut(SessionEntry entry, string sessionId, string? cliName, string chunk)
    {
        AppendScrollback(entry, chunk);
        _ = entry.OnOutput(sessionId, chunk);
        ViewerEntry[] viewers;
        lock (_gate)
        {
            viewers = [.. entry.Viewers];
        }

        foreach (var viewer in viewers)
        {
            _ = viewer.OnOutput(sessionId, chunk);
        }

        if (entry.OnChunk is { } tap)
        {
            _ = tap(sessionId, chunk);
        }

        _outputSink?.ObserveOutput(entry.AttentionKey ?? $"term:{sessionId}", cliName, chunk);
    }

    /// <summary>
    /// SPEC-20261104-run-terminal-runtime RF-003: orchestrator input —
    /// bypasses the owning-connection check but only reaches runtime-owned
    /// sessions (run terminals), never user tabs.
    /// </summary>
    public async Task<bool> InjectAsync(string sessionId, string data)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry) || !entry.RuntimeOwned)
        {
            return false;
        }

        await entry.Session.WriteAsync(data).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// RF-007: attaches a read+type viewer to a session (human attach on a run
    /// terminal). Replays the buffered scrollback like a rebind. Returns
    /// false when the session is gone.
    /// </summary>
    public bool AttachViewer(
        string userKey,
        string connectionId,
        string sessionId,
        Func<string, string, Task> onOutput,
        Func<string, string, Task> onClosed)
    {
        string replay;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var entry) || !entry.Session.IsRunning)
            {
                return false;
            }

            var existing = entry.Viewers.FindIndex(v => v.ConnectionId == connectionId);
            if (existing >= 0)
            {
                entry.Viewers[existing].OnOutput = onOutput;
                entry.Viewers[existing].OnClosed = onClosed;
            }
            else
            {
                entry.Viewers.Add(new ViewerEntry
                {
                    ConnectionId = connectionId,
                    UserKey = userKey,
                    OnOutput = onOutput,
                    OnClosed = onClosed,
                });
            }

            replay = entry.Scrollback.ToString();
        }

        if (replay.Length > 0)
        {
            _ = onOutput(sessionId, replay);
        }

        _logger.LogInformation(
            "Terminal session {SessionId} gained viewer on connection {ConnectionId}.",
            sessionId, connectionId);
        return true;
    }

    /// <summary>Detaches a viewer; fires the viewers-empty hook on the last one.</summary>
    public void DetachViewer(string sessionId, string connectionId)
    {
        Func<string, Task>? hook = null;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var entry))
            {
                return;
            }

            entry.Viewers.RemoveAll(v => v.ConnectionId == connectionId);
            if (entry.Viewers.Count == 0)
            {
                hook = entry.OnViewersEmpty;
            }
        }

        if (hook is not null)
        {
            _ = hook(sessionId);
        }
    }

    /// <summary>Viewer count of a session — used by the control-owner model.</summary>
    public int ViewerCount(string sessionId)
    {
        lock (_gate)
        {
            return _sessions.TryGetValue(sessionId, out var entry) ? entry.Viewers.Count : 0;
        }
    }

    /// <summary>Clears the runtime pin — idle reaping resumes for the session.</summary>
    public void Unpin(string sessionId)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(sessionId, out var entry))
            {
                entry.RuntimeOwned = false;
            }
        }
    }

    /// <summary>
    /// Closes a session owned by this connection; for viewers it detaches
    /// instead (RF-007 — closing an attached run-terminal tab must not kill
    /// the run). Idempotent.
    /// </summary>
    public async Task CloseAsync(string userKey, string connectionId, string sessionId)
    {
        if (!TryGetOwned(userKey, connectionId, sessionId, out _))
        {
            DetachViewer(sessionId, connectionId);
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
        foreach (var pair in _sessions.Where(pair => pair.Value.ConnectionId == connectionId))
        {
            pair.Value.ConnectionId = null;
            pair.Value.OrphanedAtUtc = now;
            _logger.LogInformation(
                "Terminal session {SessionId} orphaned (connection {ConnectionId} ended).",
                pair.Key, connectionId);
        }

        // RF-007: a dropped viewer connection detaches from every session it
        // watched — the last viewer out returns control to the runtime.
        foreach (var pair in _sessions.Where(pair => pair.Value.Viewers.Count > 0))
        {
            DetachViewer(pair.Key, connectionId);
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
            // Runtime-owned PTYs (run terminals) are exempt from the idle
            // reaper while pinned — output activity, not input, is their
            // heartbeat; the orchestrator owns the lifecycle.
            var idleExpired = !pair.Value.RuntimeOwned
                && now - pair.Value.Session.LastActivityUtc > _idleTimeout;
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

    private async Task NotifyClosedAsync(string sessionId, string reason, int? exitCode = null)
    {
        if (_sessions.TryRemove(sessionId, out var entry))
        {
            _logger.LogInformation(
                "Terminal session {SessionId} closed ({Reason}).", sessionId, reason);

            // Run-terminal exit hook first — the row records finish while the
            // scrollback tail is still intact for archival.
            if (entry.OnExited is { } onExited)
            {
                string tail;
                lock (_gate)
                {
                    tail = entry.Scrollback.ToString();
                }

                try
                {
                    await onExited(sessionId, exitCode ?? -1, tail).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Session {SessionId} exit hook failed.", sessionId);
                }
            }

            await NotifyAsync(entry, sessionId, reason).ConfigureAwait(false);
            ViewerEntry[] viewers;
            lock (_gate)
            {
                viewers = [.. entry.Viewers];
                entry.Viewers.Clear();
            }

            foreach (var viewer in viewers)
            {
                try
                {
                    await viewer.OnClosed(sessionId, reason).ConfigureAwait(false);
                }
                catch
                {
                    // Viewer may be gone; closing still proceeds.
                }
            }
            // RF-007: process exit is a lifecycle signal — strong, commits
            // immediately as exited (never inferred from silence).
            if (reason == "exited" && _attention is not null && entry.AttentionKey is { } key)
            {
                try
                {
                    await _attention.PublishAsync(new Taskboard.Application.Contracts.Agents.AttentionSignal(
                        key, "terminal", Taskboard.Agents.AttentionSignalKind.Lifecycle,
                        Taskboard.Agents.AgentAttentionState.Exited, "pty-exited"),
                        _sweepCts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // Attention publish is advisory — session teardown proceeds.
                }
            }
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

    private async Task DisposeEntryAsync(SessionEntry entry)
    {
        if (entry.AttentionKey is { } key)
        {
            _outputSink?.Discard(key);
        }
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
