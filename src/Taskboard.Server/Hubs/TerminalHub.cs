using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Harness;
using Taskboard.Integrations.Terminal;
using Taskboard.Integrations.Workspace;
using Taskboard.Server.Services;

namespace Taskboard.Server.Hubs;

/// <summary>
/// SignalR hub streaming interactive bash PTYs — several tabbed sessions per
/// user multiplexed over one connection (SPEC-20260917-terminal-tabs). All
/// lifecycle/routing lives in <see cref="TerminalSessionManager"/>.
/// </summary>
public sealed class TerminalHub : Hub
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TerminalHub> _logger;
    private readonly TerminalSessionManager _sessionManager;
    private readonly IHubContext<TerminalHub> _hubContext;
    private readonly WorkspaceService _workspace;
    private readonly IWorktreeSessionRepository _worktreeSessions;
    private readonly IServiceScopeFactory _scopeFactory;

    public TerminalHub(
        IConfiguration configuration,
        ILogger<TerminalHub> logger,
        TerminalSessionManager sessionManager,
        IHubContext<TerminalHub> hubContext,
        WorkspaceService workspace,
        IWorktreeSessionRepository worktreeSessions,
        IServiceScopeFactory scopeFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _sessionManager = sessionManager;
        _hubContext = hubContext;
        _workspace = workspace;
        _worktreeSessions = worktreeSessions;
        _scopeFactory = scopeFactory;
    }

    public override async Task OnConnectedAsync()
    {
        var flag = _configuration["Taskboard:Terminal:Enabled"];
        if (bool.TryParse(flag, out var enabled) && !enabled)
        {
            throw new HubException("Terminal disabled");
        }

        _logger.LogInformation("Terminal connection {ConnectionId} established.", Context.ConnectionId);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Sessions outlive the connection — a reconnected client reattaches
        // (SPEC-20260920-terminal-pty-resize RF-003).
        await _sessionManager.OrphanAllForConnectionAsync(Context.ConnectionId).ConfigureAwait(false);
        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens a new PTY session; returns its sessionId. SPEC-20260920 RF-006:
    /// <paramref name="repo"/> (owner/name) resolves the clone workdir
    /// server-side — only this new session gets the cwd; null keeps homeDir.
    /// Required parameter — SignalR binds by exact argument count (no optional
    /// parameters/overloads), so clients pass null for the default behavior.
    /// </summary>
    public async Task<string> Open(string? repo)
    {
        var workdir = string.IsNullOrWhiteSpace(repo)
            ? null
            : _workspace.ResolveCardWorkdir(repo, out _);
        return await OpenCoreAsync(workdir).ConfigureAwait(false);
    }

    /// <summary>
    /// SPEC-20260923-cockpit-run-hardening RF-006: opens a PTY rooted at the
    /// run's persisted worktree. The path is resolved server-side from the
    /// worktree session and must stay under the configured worktree root —
    /// client-supplied paths are never trusted.
    /// </summary>
    public async Task<string> OpenForRun(string runId)
    {
        var session = await _worktreeSessions.GetByRunIdAsync(runId, Context.ConnectionAborted)
            .ConfigureAwait(false);
        var worktreeRoot = WorktreePaths.ResolveRoot(
            _configuration["Taskboard:WorktreeRoot"], _workspace.HomeDirectory);
        if (session is null
            || !Directory.Exists(session.Path)
            || !WorktreePaths.IsUnder(worktreeRoot, session.Path))
        {
            throw new HubException("Run has no worktree");
        }

        return await OpenCoreAsync(session.Path).ConfigureAwait(false);
    }

    /// <summary>
    /// SPEC-20260928-ai-code-generic-cli RF-003: opens (or rebinds to) the
    /// PTY session backing an AI Code "terminal" thread — the CLI and workdir
    /// are resolved server-side from the thread; the session id is
    /// deterministic (<c>t-&lt;threadId&gt;</c>) so a browser refresh reattaches
    /// to the same PTY.
    /// </summary>
    public async Task<string> OpenForThread(string threadId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ThreadPtyResolver>();
        var (resolution, error) = await resolver.ResolveAsync(threadId, Context.ConnectionAborted)
            .ConfigureAwait(false);
        if (resolution is null)
        {
            throw new HubException(error ?? "Cannot resolve thread CLI.");
        }

        return await OpenCoreAsync(
            resolution.WorkingDirectory,
            resolution.Command,
            ThreadPtyResolver.SessionIdFor(threadId)).ConfigureAwait(false);
    }

    private async Task<string> OpenCoreAsync(
        string? workdir, IReadOnlyList<string>? command = null, string? sessionId = null)
    {
        var connectionId = Context.ConnectionId;

        // Hub instances are transient per invocation — callbacks must use the
        // injected IHubContext with the captured connectionId.
        try
        {
            return await _sessionManager.OpenAsync(
                UserKey,
                connectionId,
                (sessionId, chunk) => _hubContext.Clients.Client(connectionId)
                    .SendAsync("output", sessionId, chunk, CancellationToken.None),
                (sessionId, reason) => _hubContext.Clients.Client(connectionId)
                    .SendAsync("closed", sessionId, reason, CancellationToken.None),
                workdir,
                command,
                sessionId).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }
    }

    /// <summary>Writes raw input to a session.</summary>
    public async Task Input(string sessionId, string data)
    {
        await _sessionManager.InputAsync(UserKey, Context.ConnectionId, sessionId, data)
            .ConfigureAwait(false);
    }

    /// <summary>Resizes a session (cols × rows).</summary>
    public async Task Resize(string sessionId, int cols, int rows)
    {
        await _sessionManager.ResizeAsync(UserKey, Context.ConnectionId, sessionId, cols, rows)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Rebinds an orphaned session to this connection after a reconnect;
    /// returns false when the session is gone (reaped, exited, foreign).
    /// On success the buffered scrollback is replayed as a normal "output"
    /// event before live chunks (SPEC-20260928-ai-code-generic-cli RF-003).
    /// </summary>
    public async Task<bool> Reattach(string sessionId)
    {
        var connectionId = Context.ConnectionId;
        var ok = await _sessionManager.ReattachAsync(
            UserKey,
            connectionId,
            sessionId,
            (id, chunk) => _hubContext.Clients.Client(connectionId)
                .SendAsync("output", id, chunk, CancellationToken.None),
            (id, reason) => _hubContext.Clients.Client(connectionId)
                .SendAsync("closed", id, reason, CancellationToken.None)).ConfigureAwait(false);
        if (!ok)
        {
            return false;
        }

        var scrollback = _sessionManager.GetScrollback(sessionId);
        if (scrollback.Length > 0)
        {
            await _hubContext.Clients.Client(connectionId)
                .SendAsync("output", sessionId, scrollback, CancellationToken.None)
                .ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>Closes a session (tab closed by the user).</summary>
    public async Task Close(string sessionId)
    {
        await _sessionManager.CloseAsync(UserKey, Context.ConnectionId, sessionId)
            .ConfigureAwait(false);
    }

    private string UserKey => Context.User?.Identity?.Name ?? Context.ConnectionId;
}
