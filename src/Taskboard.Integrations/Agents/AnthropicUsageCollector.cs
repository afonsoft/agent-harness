using System.Net.Http.Headers;
using System.Text.Json;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// SPEC-20261020-provider-quota-usage RF-002/P3: Anthropic quota via the
/// OAuth usage endpoint (<c>GET /api/oauth/usage</c>) — the same data the
/// Claude CLI shows in <c>/usage</c>: five_hour / seven_day /
/// seven_day_opus utilizations + reset timestamps.
/// Token resolution: <c>CLAUDE_CODE_OAUTH_TOKEN</c> env →
/// <c>~/.claude/.credentials.json</c> (claudeAiOauth.accessToken, expired
/// tokens rejected). The token is only sent to api.anthropic.com over
/// HTTPS — never logged or persisted; RawJson is not stored.
/// No token / non-200 / timeout → empty (cli/local_estimate take over).
/// </summary>
public sealed class AnthropicUsageCollector : IProviderUsageCollector
{
    private readonly HttpClient _http;
    private readonly string _credentialsPath;
    private readonly Func<string?> _envToken;
    private readonly Func<DateTime> _now;

    public AnthropicUsageCollector(
        HttpClient http, string? credentialsPath = null,
        Func<string?>? envToken = null, Func<DateTime>? now = null)
    {
        _http = http;
        _credentialsPath = credentialsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", ".credentials.json");
        _envToken = envToken ?? (() => Environment.GetEnvironmentVariable(
            "CLAUDE_CODE_OAUTH_TOKEN"));
        _now = now ?? (() => DateTime.UtcNow);
    }

    public string Provider => "claude";

    public async Task<IReadOnlyList<ProviderUsageWindowDto>> CollectAsync(
        string userId, CancellationToken ct = default)
    {
        var token = await ResolveTokenAsync(ct).ConfigureAwait(false);
        if (token is null)
        {
            return [];
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get, "api/oauth/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");

        try
        {
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return AnthropicUsageParser.Parse(json, _now());
        }
        catch (HttpRequestException)
        {
            return []; // offline / DNS / TLS — cli/local_estimate take over.
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return []; // HttpClient.Timeout — degraded, not fatal.
        }
    }

    /// <summary>Env token wins; else the access token from credentials.json
    /// (expired tokens are treated as absent).</summary>
    private async Task<string?> ResolveTokenAsync(CancellationToken ct)
    {
        if (_envToken() is { Length: > 0 } env)
        {
            return env;
        }

        try
        {
            if (!File.Exists(_credentialsPath))
            {
                return null;
            }

            var raw = await File.ReadAllTextAsync(_credentialsPath, ct)
                .ConfigureAwait(false);
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth)
                || oauth.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (oauth.TryGetProperty("expiresAt", out var exp)
                && exp.ValueKind == JsonValueKind.Number
                && exp.TryGetInt64(out var ms)
                && DateTimeOffset.FromUnixTimeMilliseconds(ms) <= _now())
            {
                return null; // expired — claude /usage refresh path owns renewal.
            }

            return oauth.TryGetProperty("accessToken", out var at)
                && at.ValueKind == JsonValueKind.String
                ? at.GetString()
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// Parses <c>api/oauth/usage</c> JSON: <c>five_hour</c>,
/// <c>seven_day</c>, <c>seven_day_opus</c> — each
/// <c>{utilization: 0–100, resets_at: ISO-8601}</c>. The two weekly buckets
/// collapse to one row keeping the higher utilization.
/// </summary>
internal static class AnthropicUsageParser
{
    public static IReadOnlyList<ProviderUsageWindowDto> Parse(
        string json, DateTime nowUtc)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var windows = new List<ProviderUsageWindowDto>();
            AddWindow(windows, doc.RootElement, "five_hour",
                ProviderWindowKinds.FiveHour, nowUtc);
            AddWindow(windows, doc.RootElement, "seven_day",
                ProviderWindowKinds.Weekly, nowUtc);
            AddWindow(windows, doc.RootElement, "seven_day_opus",
                ProviderWindowKinds.Weekly, nowUtc);
            AddWindow(windows, doc.RootElement, "seven_day_sonnet",
                ProviderWindowKinds.Weekly, nowUtc);

            return windows
                .GroupBy(w => w.WindowKind, StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(w => w.UsedPct ?? 0).First())
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void AddWindow(
        List<ProviderUsageWindowDto> windows, JsonElement root,
        string property, string kind, DateTime nowUtc)
    {
        if (!root.TryGetProperty(property, out var w)
            || w.ValueKind != JsonValueKind.Object
            || !w.TryGetProperty("utilization", out var u)
            || u.ValueKind != JsonValueKind.Number
            || !u.TryGetDouble(out var pct))
        {
            return;
        }

        var resets = w.TryGetProperty("resets_at", out var r)
            && r.ValueKind == JsonValueKind.String
            && DateTime.TryParse(r.GetString(),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal
                    | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var at)
                ? at
                : (DateTime?)null;

        windows.Add(new ProviderUsageWindowDto(
            "claude", kind, Used: null, Limit: null, pct, resets, nowUtc,
            ProviderUsageSources.Api, ProviderUsageConfidences.Measured));
    }
}
