using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Domain.Entities.Chat;
using Taskboard.Repositories;
using Taskboard.ValueObjects;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// SPEC-20261015-chat-preview-panel RF-002: <c>register_preview</c> — the
/// agent announces the local app it just started so the chat Preview tab
/// (and the header "app live" dot) points at it. Loopback URLs normalize to
/// the same-origin <c>/preview/{port}/{path}</c> proxy path.
/// </summary>
public sealed class RegisterPreviewTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "register_preview";
    public string Description =>
        "Register the URL of a local web app you just started (e.g. "
        + "http://localhost:5021/) so the user can open it in the chat "
        + "Preview tab. Call this after the server is listening.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "url":{"type":"string","description":"Loopback URL of the running app (http://localhost:<port>/…)"}
        },"required":["url"]}
        """;

    public string CapabilityId => "tool:register_preview";

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        if (context.ConversationId is null)
        {
            return new ChatToolResult(
                """{"ok":false,"error":"no-conversation"}""",
                Refused: true, RefusalReason: "register_preview needs a conversation context.");
        }

        if (!arguments.TryGetProperty("url", out var urlProp) || urlProp.ValueKind != JsonValueKind.String)
        {
            return new ChatToolResult("""{"ok":false,"error":"missing-url"}""", Refused: true,
                RefusalReason: "register_preview requires {url}.");
        }

        var normalized = ChatPreviewUrl.Normalize(urlProp.GetString());
        if (normalized is null)
        {
            return new ChatToolResult(
                """{"ok":false,"error":"invalid-url"}""",
                Refused: true,
                RefusalReason: "preview URL must be loopback http(s) (localhost/127.0.0.1) or a /preview/… path.");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var conversations = scope.ServiceProvider.GetRequiredService<IRepository<ChatConversation>>();
        var conversation = await conversations.GetAsync(
            ChatConversationId.From(context.ConversationId), cancellationToken).ConfigureAwait(false);
        if (conversation is null)
        {
            return new ChatToolResult("""{"ok":false,"error":"not-found"}""", Refused: true);
        }

        conversation.SetPreviewUrl(normalized, DateTime.UtcNow);
        await conversations.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ChatToolResult($$"""{"ok":true,"previewUrl":"{{normalized}}"}""");
    }
}
