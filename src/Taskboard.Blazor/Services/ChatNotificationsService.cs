using System.Text.Json.Nodes;
using BlazorBootstrap;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Taskboard.Blazor.Services;

/// <summary>
/// SPEC-20261005-chat-background-resume RF-008: singleton-side receiver for
/// chat run completions. Connects <c>/chat-run-hub</c> once the app renders
/// and fans out <c>run.completed</c> to:
///   • in-app toast (<see cref="ToastService"/>) — always available;
///   • Notification API (<c>taskboardNotify</c>) — opt-in; fires only while
///     the tab is hidden (a focused user already sees the chat update).
///
/// Preferences: global defaults live in the config catalog
/// (<c>Taskboard:Chat:Notify:Done:*</c>); a per-browser override in
/// <c>localStorage["harness.chat.notify.*"]</c> wins without touching global
/// config — same pattern as <c>harness.locale</c>.
/// </summary>
public sealed class ChatNotificationsService : IAsyncDisposable
{
    internal const string HubPath = "/chat-run-hub";
    internal const string EventName = "run.completed";
    internal const string InAppKey = "Taskboard:Chat:Notify:Done:InApp";
    internal const string BrowserKey = "Taskboard:Chat:Notify:Done:Browser";
    internal const string PushKey = "Taskboard:Chat:Notify:Done:Push";

    private readonly NavigationManager _nav;
    private readonly IJSRuntime _js;
    private readonly ToastService _toast;
    private readonly TaskboardClient _client;
    private readonly ILogger<ChatNotificationsService> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private HubConnection? _hub;
    private bool _started;

    public ChatNotificationsService(
        NavigationManager nav,
        IJSRuntime js,
        ToastService toast,
        TaskboardClient client,
        ILogger<ChatNotificationsService> logger)
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
            _hub.On<JsonObject>(EventName, payload => _ = OnRunCompletedAsync(payload));

            try
            {
                await _hub.StartAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "chat-run hub unavailable — notifications degrade to nothing");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The browser opt-in lives in Settings (feature toggle) — this prefetches
    /// permission so the first notification isn't lost to a lazy grant. Only
    /// called when the Browser pref resolves on; never on page load.
    /// </summary>
    public async Task<string> EnsureBrowserPermissionAsync() =>
        await _js.InvokeAsync<string>("taskboardNotify.ensurePermission");

    /// <summary>Per-browser override — null volta ao default global do catálogo.</summary>
    public async Task SetBrowserOverrideAsync(string pref, bool? value)
    {
        await _js.InvokeVoidAsync("taskboardNotify.setChatPref", pref, value);
        if (value == true)
        {
            await EnsureBrowserPermissionAsync();
        }
    }

    private async Task OnRunCompletedAsync(JsonObject payload)
    {
        try
        {
            var status = payload["status"]?.GetValue<string>() ?? "completed";
            var title = payload["title"]?.GetValue<string>();
            var conversationId = payload["conversationId"]?.GetValue<string>();
            var error = payload["error"]?.GetValue<string>();

            var label = title is { Length: > 0 } ? title : "Conversa";
            var (toastType, text) = status switch
            {
                "completed" => (ToastType.Success, $"{label}: resposta concluída"),
                "stopped" => (ToastType.Secondary, $"{label}: resposta interrompida"),
                "interrupted" => (ToastType.Warning, $"{label}: interrompida (servidor reiniciou — abra para retomar)"),
                _ => (ToastType.Danger, $"{label}: falhou{Suffix(error)}"),
            };

            var prefs = await ResolvePrefsAsync();

            if (prefs.InApp)
            {
                await _toast.NotifyAsync(new ToastMessage(toastType, text));
            }

            if (prefs.Browser && await _js.InvokeAsync<bool>("taskboardNotify.isHidden"))
            {
                var url = string.IsNullOrEmpty(conversationId)
                    ? _nav.ToAbsoluteUri("/ai-chat").ToString()
                    : _nav.ToAbsoluteUri($"/ai-chat?c={Uri.EscapeDataString(conversationId)}").ToString();
                await _js.InvokeAsync<bool>(
                    "taskboardNotify.notify", "Harness", text, url, $"harness-chat-{payload["runId"]?.GetValue<string>()}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "run.completed fan-out failed — the run itself is unaffected");
        }
    }

    /// <summary>Pref name → effective bool: localStorage override &gt; config catalog.</summary>
    private async Task<(bool InApp, bool Browser, bool Push)> ResolvePrefsAsync()
    {
        var configDefaults = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [InAppKey] = true,
            [BrowserKey] = false,
            [PushKey] = false,
        };
        try
        {
            var entries = await _client.GetConfigurationEntriesAsync();
            foreach (var key in configDefaults.Keys.ToArray())
            {
                var entry = entries.FirstOrDefault(e => e.Key == key);
                if (entry?.EffectiveValue is { } value
                    && bool.TryParse(value, out var parsed))
                {
                    configDefaults[key] = parsed;
                }
            }
        }
        catch
        {
            // Catálogo indisponível — defaults locais aplicam.
        }

        return (
            await ResolveOneAsync("inapp", configDefaults[InAppKey]),
            await ResolveOneAsync("browser", configDefaults[BrowserKey]),
            await ResolveOneAsync("push", configDefaults[PushKey]));
    }

    private async Task<bool> ResolveOneAsync(string prefName, bool configDefault)
    {
        try
        {
            var local = await _js.InvokeAsync<string?>("taskboardNotify.getChatPref", prefName);
            if (bool.TryParse(local, out var parsed))
            {
                return parsed;
            }
        }
        catch
        {
            // localStorage indisponível — default global vale.
        }

        return configDefault;
    }

    private static string Suffix(string? error) =>
        string.IsNullOrWhiteSpace(error) ? string.Empty : $" — {error}";

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null)
        {
            await _hub.DisposeAsync();
        }

        _gate.Dispose();
    }
}
