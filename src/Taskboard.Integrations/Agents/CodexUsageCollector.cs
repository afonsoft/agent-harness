using System.Text.Json;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// SPEC-20261020-provider-quota-usage RF-002/P2: Codex quota collector —
/// reads the newest <c>~/.codex/sessions/**/*.jsonl</c> rollout tail for the
/// last <c>rate_limits</c> snapshot Codex CLI records per turn
/// (primary ≈5h / secondary ≈weekly, used_percent + resets_in_seconds).
/// Read-only file access, ≤128KB tail, newest file only. Empty when the
/// dir/file/payload is absent — the roster badge goes stale.
/// </summary>
public sealed class CodexUsageCollector : IProviderUsageCollector
{
    private const int TailBytes = 128 * 1024;

    private readonly string _sessionsDir;
    private readonly Func<DateTime> _now;

    public CodexUsageCollector(string? home = null, Func<DateTime>? now = null)
    {
        var root = home
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _sessionsDir = Path.Combine(root, ".codex", "sessions");
        _now = now ?? (() => DateTime.UtcNow);
    }

    public string Provider => "codex";

    public async Task<IReadOnlyList<ProviderUsageWindowDto>> CollectAsync(
        CancellationToken ct = default)
    {
        var newest = NewestRollout();
        if (newest is null)
        {
            return [];
        }

        var tail = await ReadTailAsync(newest, TailBytes, ct).ConfigureAwait(false);
        return CodexUsageParser.Parse(tail, _now());
    }

    /// <summary>Newest rollout file — bounded scan, newest wins.</summary>
    private string? NewestRollout()
    {
        try
        {
            if (!Directory.Exists(_sessionsDir))
            {
                return null;
            }

            return Directory
                .EnumerateFiles(_sessionsDir, "*.jsonl", SearchOption.AllDirectories)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<string> ReadTailAsync(
        string path, int maxBytes, CancellationToken ct)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var offset = Math.Max(0, stream.Length - maxBytes);
        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        // Drop the first partial line when we seeked mid-file.
        var nl = text.IndexOf('\n', StringComparison.Ordinal);
        return offset > 0 && nl >= 0 ? text[(nl + 1)..] : text;
    }
}

/// <summary>
/// Tolerant parser for Codex rollout <c>rate_limits</c> snapshots — walks
/// each JSON line for a <c>rate_limits</c> object with
/// <c>primary</c>/<c>secondary</c> entries; the LAST occurrence wins (latest
/// turn). window_minutes ≤390 → fiveHour, ≥10080 → weekly, else daily.
/// </summary>
internal static class CodexUsageParser
{
    public static IReadOnlyList<ProviderUsageWindowDto> Parse(
        string tail, DateTime nowUtc)
    {
        var windows = new List<ProviderUsageWindowDto>();
        foreach (var line in tail.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.Contains("rate_limits", StringComparison.Ordinal))
            {
                continue;
            }

            Collect(line, nowUtc, windows);
        }

        // last write per kind wins — the newest turn is freshest.
        return windows
            .GroupBy(w => w.WindowKind, StringComparer.Ordinal)
            .Select(g => g.Last())
            .ToList();
    }

    private static void Collect(
        string line, DateTime nowUtc, List<ProviderUsageWindowDto> windows)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var limits = FindRateLimits(doc.RootElement);
            if (limits is null)
            {
                return;
            }

            foreach (var snapshot in limits.Value.EnumerateObject())
            {
                if (snapshot.Value.ValueKind == JsonValueKind.Object
                    && TryWindow(limits.Value, snapshot.Name, nowUtc) is { } dto)
                {
                    windows.Add(dto);
                }
            }
        }
        catch (JsonException)
        {
            // corrupt line — skip.
        }
    }

    /// <summary>Finds the rate_limits object wherever Codex nests it.</summary>
    private static JsonElement? FindRateLimits(JsonElement root)
    {
        var stack = new Stack<JsonElement>();
        stack.Push(root);
        var depth = 0;
        while (stack.Count > 0 && depth++ < 10_000)
        {
            var el = stack.Pop();
            if (el.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (el.TryGetProperty("rate_limits", out var rl)
                && rl.ValueKind == JsonValueKind.Object)
            {
                return rl;
            }

            foreach (var prop in el.EnumerateObject())
            {
                stack.Push(prop.Value);
            }
        }

        return null;
    }

    private static ProviderUsageWindowDto? TryWindow(
        JsonElement limits, string name, DateTime nowUtc)
    {
        if (!limits.TryGetProperty(name, out var w)
            || w.ValueKind != JsonValueKind.Object
            || !TryGetDouble(w, "used_percent", out var usedPct))
        {
            return null;
        }

        var minutes = TryGetDouble(w, "window_minutes", out var m) ? m : 0;
        // Buckets: ≥3d → weekly, >6.5h → daily, >90m → fiveHour, else rate.
        var kind = minutes >= 4320 ? ProviderWindowKinds.Weekly
            : minutes > 390 ? ProviderWindowKinds.Daily
            : minutes > 90 ? ProviderWindowKinds.FiveHour
            : ProviderWindowKinds.Rate;
        var resets = TryGetDouble(w, "resets_in_seconds", out var s)
            ? nowUtc.AddSeconds(s)
            : (DateTime?)null;

        return new ProviderUsageWindowDto(
            "codex", kind, Used: null, Limit: null, usedPct, resets, nowUtc,
            ProviderUsageSources.Cli, ProviderUsageConfidences.Measured);
    }

    private static bool TryGetDouble(JsonElement el, string name, out double value)
    {
        value = 0;
        return el.TryGetProperty(name, out var p)
            && p.ValueKind == JsonValueKind.Number
            && p.TryGetDouble(out value);
    }
}
