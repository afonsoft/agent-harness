using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// Fetches a URL and returns its readable text — the open-webui
/// <c>fetch_url</c> builtin (SPEC-20261001-ai-chat-openwebui). Only http/https
/// is allowed; HTML is stripped to text, truncated, and secret-scrubbed.
/// </summary>
public sealed class FetchUrlTool(HttpClient http, ISecretRedactor? redactor = null) : IChatTool
{
    internal const int DefaultMaxChars = 12_000;
    internal const int AbsoluteMaxChars = 40_000;

    public string Name => "fetch_url";
    public string Description =>
        "Fetch a web page or text resource over http/https and return its readable content. "
        + "Use together with web_search to read the pages a search result points to.";
    public string ParametersJson => """
        {"type":"object","properties":{"url":{"type":"string","description":"Absolute http(s) URL to fetch"},"max_chars":{"type":"integer","description":"Max characters of content to return (default 12000, max 40000)"}},"required":["url"]}
        """;

    private static readonly Regex ScriptOrStyle = new(
        @"<(script|style|noscript|template|svg)[^>]*>.*?</\1>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    private static readonly Regex HtmlComments = new(
        @"<!--.*?-->",
        RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    private static readonly Regex BlockTags = new(
        @"</?(p|div|br|li|ul|ol|tr|table|h[1-6]|section|article|header|footer|main|aside|blockquote|pre|hr|title)[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    private static readonly Regex AnyTag = new(
        @"<[^>]+>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    private static readonly Regex BlankLines = new(
        @"[ \t]*\n[ \t\n]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var url = arguments.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String
            ? u.GetString() ?? string.Empty
            : string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return new ChatToolResult(
                JsonSerializer.Serialize(new { error = "url must be an absolute http(s) URL" }),
                Refused: true, "bad url");
        }

        var maxChars = arguments.TryGetProperty("max_chars", out var m) && m.TryGetInt32(out var mc)
            ? Math.Clamp(mc, 500, AbsoluteMaxChars)
            : DefaultMaxChars;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Harness-Chat/1.0 (+fetch_url)");
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var text = contentType.Contains("html", StringComparison.OrdinalIgnoreCase)
                ? HtmlToText(raw)
                : raw;
            text = redactor?.Redact(text) ?? text;
            var truncated = text.Length > maxChars;
            if (truncated)
            {
                text = $"{text[..maxChars]}\n…(truncated at {maxChars} chars)";
            }

            return new ChatToolResult(JsonSerializer.Serialize(new
            {
                url = uri.ToString(),
                status = (int)response.StatusCode,
                contentType,
                truncated,
                content = text,
            }));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return new ChatToolResult(JsonSerializer.Serialize(new { error = $"fetch failed: {ex.Message}", url }));
        }
    }

    /// <summary>Strips HTML to readable text: drops script/style, block tags → newlines, decodes entities.</summary>
    internal static string HtmlToText(string html)
    {
        var text = ScriptOrStyle.Replace(html, "\n");
        text = HtmlComments.Replace(text, string.Empty);
        text = BlockTags.Replace(text, "\n");
        text = AnyTag.Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = BlankLines.Replace(text.Replace("\r\n", "\n", StringComparison.Ordinal), "\n");
        return text.Trim();
    }
}
