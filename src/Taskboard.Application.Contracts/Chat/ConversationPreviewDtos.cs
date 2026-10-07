using System.Text.RegularExpressions;

namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// SPEC-20261015-chat-preview-panel: preview tab wire types.
/// </summary>

/// <summary>Body of <c>POST /api/local/chat/conversations/{id}/preview</c> (RF-002).</summary>
public sealed record SetChatPreviewRequest(string Url);

/// <summary>
/// Element picked in the preview iframe (RF-004) — travels on the send
/// payload (<see cref="SendChatMessageRequest.Quote"/>), renders as a small
/// card on the user message and reaches the model as a context block.
/// </summary>
public sealed record ChatElementQuote(
    string Selector, string Tag, string? Text, string? PageUrl)
{
    /// <summary>
    /// RF-004: the quote as a markdown blockquote line prepended to the user
    /// message — renders as a small card in the transcript and reaches the
    /// model as a context block.
    /// </summary>
    public string FormatBlock()
    {
        var text = Text is { Length: > 0 } t ? $" \"{t}\"" : string.Empty;
        var page = PageUrl is { Length: > 0 } p ? $" — {p}" : string.Empty;
        return $"> `{Selector}`{text}{page}";
    }
}

/// <summary>
/// Normalizes a user/agent-supplied app URL into the same-origin proxy path
/// (<c>/preview/{port}/{path}</c>). Accepts loopback literals only —
/// <c>localhost</c>/<c>127.0.0.1</c>/<c>[::1]</c> — or an already-normalized
/// <c>/preview/…</c> path (RF-002, RNF SSRF guard).
/// </summary>
public static partial class ChatPreviewUrl
{
    public const int MinPort = 1024;
    public const int MaxPort = 65535;

    /// <summary>
    /// Returns the normalized proxy path, or null when the input is not a
    /// loopback URL / preview path with a valid port.
    /// </summary>
    public static string? Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var trimmed = url.Trim();

        var proxyMatch = ProxyPath().Match(trimmed);
        if (proxyMatch.Success)
        {
            return $"/preview/{proxyMatch.Groups[1].Value}/{proxyMatch.Groups[2].Value}";
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var host = uri.Host;
        var loopback = string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || host is "127.0.0.1" or "[::1]" or "::1";
        if (!loopback || uri.Port is < MinPort or > MaxPort)
        {
            return null;
        }

        var path = uri.PathAndQuery.TrimStart('/');
        return $"/preview/{uri.Port}/{path}";
    }

    /// <summary><c>/preview/{port}/{path}</c> — path optional, may carry a query.</summary>
    [GeneratedRegex(@"^/preview/(\d{4,5})/?(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex ProxyPath();
}
