using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Blazor;

/// <summary>
/// SPEC-20261005-chat-background-resume RF-008 source guards: tripwires on the
/// notification fan-out — hub map + notifier registration, JS pref helpers,
/// per-browser overrides UI and the /ai-chat?c= deep-link. Client-side
/// behavior is covered by the real loop (hub → service → toast/JS); these
/// tests pin the seams so a refactor can't silently drop them.
/// </summary>
public class ChatNotificationsGuardTests
{
    private static string RepoPath(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Taskboard.sln")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("could not locate the repo root (Taskboard.sln)");
        return Path.Combine([dir.FullName, .. parts]);
    }

    private static string Read(params string[] parts) => File.ReadAllText(RepoPath(parts));

    [Fact]
    public void Dado_Program_Quando_Le_Entao_MapeiaChatRunHubComAuthERegistraNotifier()
    {
        var program = Read("src", "Taskboard.Server", "Program.cs");

        program.ShouldContain("MapHub<ChatRunHub>(\"/chat-run-hub\").RequireAuthorization()");
        program.ShouldContain("AddScoped<IChatRunNotifier, SignalRChatRunNotifier>()");
    }

    [Fact]
    public void Dado_Notifier_Quando_Le_Entao_PublicaRunCompletedParaTodos()
    {
        var notifier = Read("src", "Taskboard.Server", "Services", "SignalRChatRunNotifier.cs");

        notifier.ShouldContain("IHubContext<ChatRunHub>");
        notifier.ShouldContain("run.completed");
        notifier.ShouldContain("Clients.All.SendAsync");
        notifier.ShouldContain("conversation?.Title");
    }

    [Fact]
    public void Dado_TaskboardJs_Quando_Le_Entao_HelperNotifyTemPrefsPermissaoEVisibilidade()
    {
        var js = Read("src", "Taskboard.Client", "wwwroot", "js", "taskboard.js");

        js.ShouldContain("ensurePermission: async function");
        js.ShouldContain("isHidden: function");
        js.ShouldContain("getChatPref: function");
        js.ShouldContain("setChatPref: function");
        js.ShouldContain("harness.chat.notify.");
    }

    [Fact]
    public void Dado_MainLayout_Quando_Le_Entao_IniciaChatNotificationsNoPrimeiroRender()
    {
        var layout = Read("src", "Taskboard.Blazor", "Layout", "MainLayout.razor");

        layout.ShouldContain("ChatNotificationsService ChatNotifications");
        layout.ShouldContain("ChatNotifications.EnsureStartedAsync()");
    }

    [Fact]
    public void Dado_ChatNotificationsService_Quando_Le_Entao_FanOutToastBrowserNotifyEDeepLink()
    {
        var service = Read("src", "Taskboard.Blazor", "Services", "ChatNotificationsService.cs");

        service.ShouldContain("run.completed");
        service.ShouldContain("_toast.NotifyAsync");
        service.ShouldContain("taskboardNotify.isHidden");
        service.ShouldContain("taskboardNotify.notify");
        service.ShouldContain("/ai-chat?c=");
        service.ShouldContain("Taskboard:Chat:Notify:Done:InApp");
        service.ShouldContain("taskboardNotify.getChatPref");
    }

    [Fact]
    public void Dado_SettingsEAiChat_Quando_Le_Entao_TogglesDeepLinkEOverridesWired()
    {
        var settings = Read("src", "Taskboard.Blazor", "Components", "Pages", "Settings.razor");
        var aiChat = Read("src", "Taskboard.Blazor", "Components", "Pages", "AiChat.razor");

        settings.ShouldContain("Taskboard:Chat:Notify:Done:InApp");
        settings.ShouldContain("Taskboard:Chat:Notify:Done:Browser");
        settings.ShouldContain("Taskboard:Chat:Notify:Done:Push");
        settings.ShouldContain("taskboardNotify.getChatPref");
        settings.ShouldContain("taskboardNotify.setChatPref");

        aiChat.ShouldContain("[SupplyParameterFromQuery(Name = \"c\")]");
        aiChat.ShouldContain("SeedConversationId=\"@_seedConversationId\"");
    }

    // ---- SPEC-20261005 RF-009: Web Push seams ----

    [Fact]
    public void Dado_Program_Quando_Le_Entao_MapeiaEndpointsPushERegistraWebPushNotifier()
    {
        var program = Read("src", "Taskboard.Server", "Program.cs");

        program.ShouldContain("api.MapGroup(\"local/push\").RequireAuthorization()");
        program.ShouldContain("push.MapGet(\"vapid-public\"");
        program.ShouldContain("push.MapPost(\"subscriptions\"");
        program.ShouldContain("push.MapDelete(\"subscriptions\"");
        program.ShouldContain("AddScoped<IChatRunNotifier, WebPushChatRunNotifier>()");
        program.ShouldContain("AddScoped<IWebPushSender, WebPushSender>()");
    }

    [Fact]
    public void Dado_Dispatcher_Quando_Le_Entao_FanOutParaTodosOsNotifiers()
    {
        var dispatcher = Read("src", "Taskboard.Server", "Services", "ChatRunDispatcherService.cs");

        dispatcher.ShouldContain("GetServices<IChatRunNotifier>()");
        dispatcher.ShouldContain("foreach (var notifier in notifiers)");
    }

    [Fact]
    public void Dado_PushSw_Quando_Le_Entao_ShowNotificationEOpenWindow()
    {
        var sw = Read("src", "Taskboard.Client", "wwwroot", "push-sw.js");

        sw.ShouldContain("self.addEventListener('push'");
        sw.ShouldContain("showNotification");
        sw.ShouldContain("notificationclick");
        sw.ShouldContain("clients.openWindow");
    }

    [Fact]
    public void Dado_TaskboardJs_Quando_Le_Entao_TemHelpersDePush()
    {
        var js = Read("src", "Taskboard.Client", "wwwroot", "js", "taskboard.js");

        js.ShouldContain("subscribePush: async function");
        js.ShouldContain("unsubscribePush: async function");
        js.ShouldContain("isPushSubscribed: async function");
        js.ShouldContain("pushManager.subscribe");
        js.ShouldContain("/push-sw.js");
    }

    [Fact]
    public void Dado_ChatNotificationsService_Quando_Le_Entao_SincronizaPushSubscription()
    {
        var service = Read("src", "Taskboard.Blazor", "Services", "ChatNotificationsService.cs");

        service.ShouldContain("SyncPushAsync");
        service.ShouldContain("taskboardNotify.isPushSubscribed");
        service.ShouldContain("taskboardNotify.subscribePush");
        service.ShouldContain("taskboardNotify.unsubscribePush");
        service.ShouldContain("/api/local/push/");
    }

    [Fact]
    public void Dado_Settings_Quando_Le_Entao_TogglePushChamaSync()
    {
        var settings = Read("src", "Taskboard.Blazor", "Components", "Pages", "Settings.razor");

        settings.ShouldContain("ChatNotifications.SyncPushAsync()");
    }
}
