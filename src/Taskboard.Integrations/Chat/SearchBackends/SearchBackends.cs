using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Integrations.Chat.SearchBackends;

/// <summary>SearxNG instance (self-hosted, no key) — <c>GET {url}/search?q=&format=json</c> (RF-007).</summary>
public sealed class SearxNgSearchBackend(HttpClient http, string baseUrl) : ISearchBackend
{
    public async Task<IReadOnlyList<ChatSearchResult>> SearchAsync(
        string query, int maxResults, CancellationToken cancellationToken)
    {
        var url = $"{baseUrl.TrimEnd('/')}/search?q={Uri.EscapeDataString(query)}&format=json";
        using var response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var results = new List<ChatSearchResult>();
        if (body.TryGetProperty("results", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (results.Count >= maxResults)
                {
                    break;
                }

                results.Add(new ChatSearchResult(
                    item.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "",
                    item.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() ?? "" : "",
                    item.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : ""));
            }
        }

        return results;
    }
}

/// <summary>Tavily API (key required) — <c>POST https://api.tavily.com/search</c> (RF-007).</summary>
public sealed class TavilySearchBackend(HttpClient http, string apiKey) : ISearchBackend
{
    public async Task<IReadOnlyList<ChatSearchResult>> SearchAsync(
        string query, int maxResults, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["api_key"] = apiKey,
            ["query"] = query,
            ["max_results"] = maxResults,
        };
        using var response = await http.PostAsJsonAsync("https://api.tavily.com/search", payload, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return ParseResults(body);
    }

    internal static IReadOnlyList<ChatSearchResult> ParseResults(JsonElement body)
    {
        var results = new List<ChatSearchResult>();
        if (body.TryGetProperty("results", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                results.Add(new ChatSearchResult(
                    item.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "",
                    item.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() ?? "" : "",
                    item.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : ""));
            }
        }

        return results;
    }
}

/// <summary>Brave Search API (key required) — <c>GET https://api.search.brave.com/res/v1/web/search</c> (RF-007).</summary>
public sealed class BraveSearchBackend(HttpClient http, string apiKey) : ISearchBackend
{
    public async Task<IReadOnlyList<ChatSearchResult>> SearchAsync(
        string query, int maxResults, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count={maxResults}");
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.TryAddWithoutValidation("X-Subscription-Token", apiKey);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var results = new List<ChatSearchResult>();
        if (body.TryGetProperty("web", out var web) && web.TryGetProperty("results", out var items)
            && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (results.Count >= maxResults)
                {
                    break;
                }

                results.Add(new ChatSearchResult(
                    item.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "",
                    item.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() ?? "" : "",
                    item.TryGetProperty("description", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : ""));
            }
        }

        return results;
    }
}

/// <summary>Builds the configured backend from the Settings values (RF-007).</summary>
public static class SearchBackendFactory
{
    public static ISearchBackend? Create(string kind, string url, string apiKey, HttpClient http) => kind switch
    {
        "searxng" when !string.IsNullOrWhiteSpace(url) => new SearxNgSearchBackend(http, url),
        "tavily" when !string.IsNullOrWhiteSpace(apiKey) => new TavilySearchBackend(http, apiKey),
        "brave" when !string.IsNullOrWhiteSpace(apiKey) => new BraveSearchBackend(http, apiKey),
        _ => null,
    };
}
