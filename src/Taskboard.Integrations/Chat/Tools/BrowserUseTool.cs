using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// SPEC-20261016-chat-browser-tool: <c>browser_use</c> — lets the agent drive
/// a per-conversation headless Chromium (navigate/click/type/scroll/
/// screenshot/extract_text/extract_html/eval_js) so it can see and operate
/// the web UI it builds. Confirmation-gated; screenshots persist as
/// <c>browser-shot-*</c> attachments bound to the tool message.
/// </summary>
public sealed class BrowserUseTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    /// <summary>RF-006: only http(s) — localhost allowed (it's the point).</summary>
    public static bool UrlAllowed(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https";

    public string Name => "browser_use";
    public string Description =>
        "Drive the headless browser: navigate|click|type|scroll|screenshot|"
        + "extract_text|extract_html|eval_js on a persistent per-conversation "
        + "page. Use it to test the web UI you build (e.g. the app registered "
        + "via register_preview). Screenshots appear in the Browser tab.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "action":{"type":"string","enum":["navigate","click","type","scroll","screenshot","extract_text","extract_html","eval_js"]},
          "url":{"type":"string","description":"navigate target — http(s) only"},
          "selector":{"type":"string","description":"CSS selector for click/type"},
          "text":{"type":"string","description":"type: text to enter; eval_js is 'script' instead"},
          "direction":{"type":"string","description":"scroll: up|down|left|right"},
          "fullPage":{"type":"boolean","description":"screenshot: capture the whole page"},
          "script":{"type":"string","description":"eval_js: JS expression/script to evaluate"},
          "waitMs":{"type":"integer","description":"extra settle wait after the action (max 5000)"},
          "includeConsole":{"type":"boolean","description":"append recent page console errors to the result"}
        },"required":["action"]}
        """;

    public string CapabilityId => "tool:browser_use";

    /// <summary>RF-007: every action surfaces an approval card.</summary>
    public bool RequiresConfirmation => true;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        if (context.ConversationId is null)
        {
            return Refuse("no-conversation", "browser_use needs a conversation context.");
        }

        if (!arguments.TryGetProperty("action", out var actionProp)
            || actionProp.ValueKind != JsonValueKind.String
            || !BrowserAction.Actions.Contains(actionProp.GetString()!))
        {
            return Refuse("bad-action", "browser_use requires a valid 'action'.");
        }

        var action = actionProp.GetString()!;
        var url = StringArg(arguments, "url");
        var selector = StringArg(arguments, "selector");
        var script = StringArg(arguments, "script");

        if (action == "navigate" && !UrlAllowed(url))
        {
            return Refuse("bad-url", "navigate requires an http(s) URL.");
        }

        if (action is "click" or "type" && string.IsNullOrWhiteSpace(selector))
        {
            return Refuse("missing-selector", $"{action} requires 'selector'.");
        }

        if (action == "eval_js" && string.IsNullOrWhiteSpace(script))
        {
            return Refuse("missing-script", "eval_js requires 'script'.");
        }

        var waitMs = arguments.TryGetProperty("waitMs", out var w) && w.ValueKind == JsonValueKind.Number
            ? Math.Clamp(w.GetInt32(), 0, 5000)
            : 0;

        var call = new BrowserAction(
            action, url, selector, StringArg(arguments, "text"),
            StringArg(arguments, "direction"),
            arguments.TryGetProperty("fullPage", out var fp) && fp.ValueKind == JsonValueKind.True,
            script, waitMs,
            arguments.TryGetProperty("includeConsole", out var ic) && ic.ValueKind == JsonValueKind.True);

        await using var scope = scopeFactory.CreateAsyncScope();
        var pool = scope.ServiceProvider.GetRequiredService<IBrowserSessionPool>();
        var result = await pool.ExecuteAsync(context.ConversationId, call, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Ok)
        {
            return new ChatToolResult(JsonSerializer.Serialize(new
            {
                ok = false, action, error = result.Error ?? "browser action failed",
            }));
        }

        // RF-002/RF-003: the shot becomes a browser-shot attachment; the run
        // binds it to this tool message (AttachmentIds) and it feeds the tab.
        string? shotId = null;
        string? shotUrl = null;
        if (result.ShotPng is { Length: > 0 } png)
        {
            var store = scope.ServiceProvider.GetRequiredService<IChatShotStore>();
            shotId = await store.SaveShotAsync(context.ConversationId, action, png, cancellationToken)
                .ConfigureAwait(false);
            shotUrl = $"/api/local/chat/conversations/{context.ConversationId}/attachments/{shotId}/download";
        }

        return new ChatToolResult(
            JsonSerializer.Serialize(new
            {
                ok = true, action, url = result.Url, title = result.Title,
                text = result.Text, shotId, shotUrl,
            }),
            AttachmentIds: shotId is null ? null : [shotId]);
    }

    private static ChatToolResult Refuse(string error, string reason) =>
        new(JsonSerializer.Serialize(new { ok = false, error }), Refused: true, RefusalReason: reason);

    private static string? StringArg(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
}
