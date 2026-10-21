using BlazorBootstrap;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Taskboard.Agents;
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
    private readonly IJSRuntime _js;
    private readonly ToastService _toast;
    private readonly TaskboardClient _client;
    private readonly ILogger<AgentFeedNotificationsService> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private HubConnection? _hub;
    private bool _started;
    private IReadOnlyDictionary<AgentFeedKind, FeedCategoryPreference> _prefs = new Dictionary<AgentFeedKind, FeedCategoryPreference>();

    /// <summary>Raised for every <c>agent.feed</c> push.</summary>
    public event Func<AgentFeedPushDto, Task>? FeedPushed;

    /// <summary>Raised after a successful (re)connect — consumers re-fetch.</summary>
    public event Func<Task>? Resynced;

    public AgentFeedNotificationsService(
        NavigationManager nav,
        IJSRuntime js,
        ToastService toast,
        TaskboardClient client,
        ILogger<AgentFeedNotificationsService> logger)
    {
        _nav = nav;
        _js = js;
        _toast = toast;
        _client = client;
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
            _hub.On<AgentFeedPushDto>(EventName, async push =>
            {
                _ = HandlePushEffectsAsync(push);
                var handler = FeedPushed;
                if (handler is not null)
                {
                    await handler.Invoke(push);
                }
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

            await RefreshPrefsAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>RF-006: reload merged prefs — call after saving in /settings.</summary>
    public async Task RefreshPrefsAsync()
    {
        try
        {
            var prefs = await _client.GetAgentFeedPreferencesAsync();
            if (prefs is not null)
            {
                _prefs = prefs.Categories;
            }
        }
        catch
        {
            // defaults suffice — prefs fetch is advisory
        }
    }

    /// <summary>
    /// RF-006/RF-007: side effects per push — ephemeral items toast (never in
    /// feed); attention/question items chime when the category's sound pref
    /// is on and the global mute is off.
    /// </summary>
    private async Task HandlePushEffectsAsync(AgentFeedPushDto push)
    {
        if (push.Item is not { } item)
        {
            return;
        }

        var pref = _prefs.TryGetValue(item.Kind, out var p)
            ? p
            : AgentFeedDefaults.For(item.Kind);

        if (push.Kind == "ephemeral")
        {
            try
            {
                await _toast.NotifyAsync(new ToastMessage(ToastType.Info, item.Title));
            }
            catch
            {
                // toast surface unavailable — ignore
            }
        }

        if (pref.Sound && (item.Kind is AgentFeedKind.Attention or AgentFeedKind.Question))
        {
            try
            {
                await _js.InvokeVoidAsync("taskboardNotify.playFeedChime");
            }
            catch
            {
                // muted / audio unsupported — degrade silently
            }
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
