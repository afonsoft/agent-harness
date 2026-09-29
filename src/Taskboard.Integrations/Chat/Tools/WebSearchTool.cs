using System.Text.Json;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// Web search behind the configured backend (RF-007) — SearxNG, Tavily or
/// Brave. Results (title/url/snippet) are data injected as tool output; URLs
/// are never fetched automatically.
/// </summary>
public sealed class WebSearchTool(IReadOnlyDictionary<string, ISearchBackend> backends, int maxResults = 5) : IChatTool
{
    public string Name => "web_search";
    public string Description => "Search the internet and return the top results (title, url, snippet).";
    public string ParametersJson => """
        {"type":"object","properties":{"query":{"type":"string","description":"Search query"}},"required":["query"]}
        """;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var query = arguments.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String
            ? q.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            return new ChatToolResult(JsonSerializer.Serialize(new { error = "query is required" }), Refused: true, "empty query");
        }

        if (context.SearchBackend is "none" or "" || !backends.TryGetValue(context.SearchBackend, out var backend))
        {
            return new ChatToolResult(JsonSerializer.Serialize(new
            {
                error = "web search is not configured — set Taskboard:Chat:SearchBackend (searxng|tavily|brave) in Settings",
            }));
        }

        try
        {
            var results = await backend.SearchAsync(query, maxResults, cancellationToken).ConfigureAwait(false);
            return new ChatToolResult(JsonSerializer.Serialize(new
            {
                query,
                results = results.Select(r => new { title = r.Title, url = r.Url, snippet = r.Snippet }).ToList(),
            }));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return new ChatToolResult(JsonSerializer.Serialize(new { error = $"search backend failed: {ex.Message}" }));
        }
    }
}
