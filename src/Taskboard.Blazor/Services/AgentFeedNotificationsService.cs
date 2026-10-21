using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Blazor.Services;

/// <summary>
/// SPEC-20261020-agents-feed-notifications RF-010: singleton-side receiver for
/// <c>agent.feed</c> pushes on <c>/agent-feed-hub</c>. Joins the board user's
/// feed group once connected and fans events out to subscribers (nav badge,
/// feed panel). On reconnect the group is re-joined and <see cref="Resynced"/>
/// fires so consumers re-fetch from their saved cursor.
/// </summary>
public sealed class AgentFeedNotificationsService : IAsyncDisposable
{
    internal const string HubPath = "/agent-feed-hub";
    internal const string EventName = "agent.feed";
    internal const string UserId = "local";

    private readonly NavigationManager _nav;
    private readonly ILogger<AgentFeedNotificationsService> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private HubConnection? _hub;
    private bool _started;

    /// <summary>Raised for every <c>agent.feed</c> push.</summary>
    public event Func<AgentFeedPushDto, Task>? FeedPushed;

    /// <summary>Raised after a successful (re)connect — consumers re-fetch.</summary>
    public event Func<Task>? Resynced;

    public AgentFeedNotificationsService(
        NavigationManager nav,
        ILogger<AgentFeedNotificationsService> logger)
    {
        _nav = nav;
        _logger = logger;
    }

    /// <summary>Idempotent connect — safe to call from MainLayout on every nav.</summary>
    public async Task EnsureStartedAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _hub = new HubConnectionBuilder()
                .WithUrl(_nav.ToAbsoluteUri(HubPath))
                .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)])
                .Build();
            _hub.On<AgentFeedPushDto>(EventName, push =>
            {
                var handler = FeedPushed;
                return handler?.Invoke(push) ?? Task.CompletedTask;
            });
            _hub.Reconnected += async _ =>
            {
                await JoinFeedGroupAsync();
                var handler = Resynced;
                if (handler is not null)
                {
                    await handler.Invoke();
                }
            };

            try
            {
                await _hub.StartAsync();
                await JoinFeedGroupAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "agent-feed hub unavailable — badge degrades to REST");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task JoinFeedGroupAsync()
    {
        if (_hub is { State: HubConnectionState.Connected } hub)
        {
            await hub.InvokeAsync("JoinFeedGroup", UserId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null)
        {
            try
            {
                await _hub.DisposeAsync();
            }
            catch
            {
                // best-effort teardown
            }
        }
    }
}
