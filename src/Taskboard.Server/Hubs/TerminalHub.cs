using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Taskboard.Integrations.Terminal;

namespace Taskboard.Server.Hubs;

/// <summary>
/// SignalR hub streaming an interactive bash PTY per authenticated user
/// (SPEC-20260917-cli-agents-terminal RF-005). One session per user; idle
/// sessions are closed after <see cref="IdleTimeout"/>.
/// </summary>
public sealed class TerminalHub : Hub
{
    internal static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    private sealed record SessionEntry(PtySession Session, string ConnectionId, CancellationTokenSource IdleWatch);

    private static readonly ConcurrentDictionary<string, SessionEntry> Sessions = new();

    private readonly IConfiguration _configuration;
    private readonly ILogger<TerminalHub> _logger;
    private readonly PtySessionFactory _sessionFactory;
    private readonly IHubContext<TerminalHub> _hubContext;

    public TerminalHub(
        IConfiguration configuration,
        ILogger<TerminalHub> logger,
        PtySessionFactory sessionFactory,
        IHubContext<TerminalHub> hubContext)
    {
        _configuration = configuration;
        _logger = logger;
        _sessionFactory = sessionFactory;
        _hubContext = hubContext;
    }

    public override async Task OnConnectedAsync()
    {
        var flag = _configuration["Taskboard:Terminal:Enabled"];
        if (bool.TryParse(flag, out var enabled) && !enabled)
        {
            throw new HubException("Terminal disabled");
        }

        var userKey = Context.User?.Identity?.Name ?? Context.ConnectionId;

        if (Sessions.TryRemove(userKey, out var existing))
        {
            try
            {
                await Clients.Client(existing.ConnectionId)
                    .SendAsync("closed", "replaced", Context.ConnectionAborted)
                    .ConfigureAwait(false);
            }
            catch
            {
            }

            await DisposeSessionAsync(existing).ConfigureAwait(false);
        }

        var session = _sessionFactory.Create();
        var idleWatch = new CancellationTokenSource();
        var connectionId = Context.ConnectionId;
        var entry = new SessionEntry(session, connectionId, idleWatch);

        // Hub instances are transient per invocation — event handlers must use
        // the injected IHubContext, not `Clients` (this instance is disposed
        // when OnConnectedAsync returns).
        session.OutputReceived += chunk =>
        {
            _ = _hubContext.Clients.Client(connectionId)
                .SendAsync("output", chunk, CancellationToken.None);
        };
        session.Exited += exitCode =>
        {
            _ = _hubContext.Clients.Client(connectionId)
                .SendAsync("closed", "exited", CancellationToken.None);
        };

        session.Start();
        Sessions[userKey] = entry;
        _ = WatchIdleAsync(userKey, session, idleWatch.Token);

        _logger.LogInformation(
            "Terminal session started for {User} (connection {ConnectionId}).", userKey, connectionId);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (TryGetEntry(out var userKey, out var entry))
        {
            _logger.LogInformation("Terminal session ended for {User}.", userKey);
            await DisposeSessionAsync(entry).ConfigureAwait(false);
        }

        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    /// <summary>Writes raw input to the PTY.</summary>
    public async Task Input(string data)
    {
        if (TryGetEntry(out _, out var entry))
        {
            await entry.Session.WriteAsync(data).ConfigureAwait(false);
        }
    }

    /// <summary>Resizes the PTY (cols × rows).</summary>
    public async Task Resize(int cols, int rows)
    {
        if (TryGetEntry(out _, out var entry))
        {
            await entry.Session.ResizeAsync(cols, rows).ConfigureAwait(false);
        }
    }

    private bool TryGetEntry(out string userKey, out SessionEntry entry)
    {
        userKey = Context.User?.Identity?.Name ?? Context.ConnectionId;
        return Sessions.TryGetValue(userKey, out entry!)
            && entry.ConnectionId == Context.ConnectionId;
    }

    private async Task WatchIdleAsync(string userKey, PtySession session, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (DateTimeOffset.UtcNow - session.LastActivityUtc <= IdleTimeout)
                {
                    continue;
                }

                if (Sessions.TryRemove(userKey, out var entry))
                {
                    try
                    {
                        await _hubContext.Clients.Client(entry.ConnectionId)
                            .SendAsync("closed", "idle-timeout", CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                    }

                    _logger.LogInformation("Terminal session for {User} closed after idle timeout.", userKey);
                    await DisposeSessionAsync(entry).ConfigureAwait(false);
                }

                return;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task DisposeSessionAsync(SessionEntry entry)
    {
        try
        {
            await entry.IdleWatch.CancelAsync().ConfigureAwait(false);
            entry.IdleWatch.Dispose();
        }
        catch
        {
        }

        await entry.Session.DisposeAsync().ConfigureAwait(false);
    }
}
