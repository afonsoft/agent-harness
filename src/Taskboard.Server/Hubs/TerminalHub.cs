using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Harness;
using Taskboard.Integrations.Terminal;
using Taskboard.Integrations.Workspace;

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

    public TerminalHub(
        IConfiguration configuration,
        ILogger<TerminalHub> logger,
        TerminalSessionManager sessionManager,
        IHubContext<TerminalHub> hubContext,
        WorkspaceService workspace,
        IWorktreeSessionRepository worktreeSessions)
    {
        _configuration = configuration;
        _logger = logger;
        _sessionManager = sessionManager;
        _hubContext = hubContext;
        _workspace = workspace;
        _worktreeSessions = worktreeSessions;
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

    private async Task<string> OpenCoreAsync(string? workdir)
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
                workdir).ConfigureAwait(false);
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
    /// </summary>
    public Task<bool> Reattach(string sessionId)
    {
        var connectionId = Context.ConnectionId;
        return _sessionManager.ReattachAsync(
            UserKey,
            connectionId,
            sessionId,
            (id, chunk) => _hubContext.Clients.Client(connectionId)
                .SendAsync("output", id, chunk, CancellationToken.None),
            (id, reason) => _hubContext.Clients.Client(connectionId)
                .SendAsync("closed", id, reason, CancellationToken.None));
    }

    /// <summary>Closes a session (tab closed by the user).</summary>
    public async Task Close(string sessionId)
    {
        await _sessionManager.CloseAsync(UserKey, Context.ConnectionId, sessionId)
            .ConfigureAwait(false);
    }

    private string UserKey => Context.User?.Identity?.Name ?? Context.ConnectionId;
}
