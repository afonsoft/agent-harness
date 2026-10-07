namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// SPEC-20261016-chat-browser-tool: <c>browser_use</c> wire types + the
/// pool/store abstractions the tool talks to (kept out of the tool so tests
/// fake them without a browser).
/// </summary>

/// <summary>One <c>browser_use</c> invocation, already argument-validated.</summary>
public sealed record BrowserAction(
    string Action,
    string? Url = null,
    string? Selector = null,
    string? Text = null,
    string? Direction = null,
    bool FullPage = false,
    string? Script = null,
    int WaitMs = 0,
    bool IncludeConsole = false)
{
    public static readonly IReadOnlySet<string> Actions = new HashSet<string>(StringComparer.Ordinal)
    {
        "navigate", "click", "type", "scroll", "screenshot",
        "extract_text", "extract_html", "eval_js",
    };
}

/// <summary>Result of a browser action; <see cref="ShotPng"/> is set when a screenshot was taken.</summary>
public sealed record BrowserActionResult(
    bool Ok,
    string? Url,
    string? Title,
    string? Text = null,
    byte[]? ShotPng = null,
    string? Error = null);

/// <summary>
/// Per-conversation headless-Chromium sessions (RF-001/RF-005) — pool is a
/// singleton; the tool resolves it per call via DI.
/// </summary>
public interface IBrowserSessionPool
{
    Task<BrowserActionResult> ExecuteAsync(
        string conversationId, BrowserAction action, CancellationToken cancellationToken);
}

/// <summary>
/// Screenshot persistence (RF-003): shots land as <see cref="ChatAttachment"/>
/// rows with the <c>browser-shot-</c> filename prefix so they render inline on
/// the tool message and feed the Browser tab; the row id is returned so the
/// run binds it to the tool message.
/// </summary>
public interface IChatShotStore
{
    /// <summary>Saves the PNG bytes; returns the attachment id to bind.</summary>
    Task<string> SaveShotAsync(
        string conversationId, string action, byte[] png, CancellationToken cancellationToken);

    /// <summary>Latest-first list of a conversation's shots (RF-004 gallery).</summary>
    Task<IReadOnlyList<BrowserShotDto>> ListShotsAsync(
        string conversationId, CancellationToken cancellationToken);
}

/// <summary>One shot in the Browser tab gallery.</summary>
public sealed record BrowserShotDto(
    string Id, string FileName, string DownloadUrl, DateTime CreatedAt);
