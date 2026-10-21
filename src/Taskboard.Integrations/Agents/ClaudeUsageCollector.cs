using System.Text.RegularExpressions;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Agents;
using Taskboard.Integrations.Execution;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// SPEC-20261020-provider-quota-usage RF-002: Claude quota collector — runs
/// <c>claude /usage</c> (read-only, 10s timeout) and parses the window blocks
/// (Current session → fiveHour, Current day → daily, Current week → weekly).
/// Empty on timeout/non-zero exit/unparseable output — the job keeps prior
/// samples and the roster badge goes stale.
/// </summary>
public sealed class ClaudeUsageCollector : IProviderUsageCollector
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly IProcessRunner _runner;
    private readonly string _workdir;

    public ClaudeUsageCollector(IProcessRunner runner, string? workdir = null)
    {
        _runner = runner;
        _workdir = workdir ?? Directory.GetCurrentDirectory();
    }

    public string Provider => "claude";

    public async Task<IReadOnlyList<ProviderUsageWindowDto>> CollectAsync(
        CancellationToken ct = default)
    {
        var result = await _runner.RunAsync(
            "claude", _workdir, ["/usage"], Timeout, ct);
        if (result.TimedOut || result.ExitCode != 0)
        {
            return [];
        }

        return ClaudeUsageParser.Parse(result.StdOut, DateTime.UtcNow);
    }
}

/// <summary>
/// Parses <c>claude /usage</c> output into quota windows. Tolerant: blocks
/// separated by blank lines carry a label line, a "NN% used" line and an
/// optional "Resets in/at …" line; unparseable blocks are skipped. One row
/// per (provider, windowKind) — the highest-pct block wins when several
/// blocks share a kind (e.g. "all models" vs "Sonnet only").
/// </summary>
internal static class ClaudeUsageParser
{
    private static readonly Regex PctUsed =
        new(@"(\d+(?:\.\d+)?)\s*%\s*used", RegexOptions.IgnoreCase | RegexOptions.Compiled,
            TimeSpan.FromSeconds(1));

    private static readonly Regex ResetsIn =
        new(@"Resets\s+in\s+(?:(\d+)\s*h(?:ours?)?)?\s*(?:(\d+)\s*m(?:in)?)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private static readonly Regex ResetsAt =
        new(@"Resets\s+at\s+(\d{1,2})(?::(\d{2}))?\s*(am|pm)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public static IReadOnlyList<ProviderUsageWindowDto> Parse(
        string output, DateTime nowUtc)
    {
        var windows = new Dictionary<string, ProviderUsageWindowDto>(StringComparer.Ordinal);
        foreach (var block in output.Split(
                     ["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var pct = PctUsed.Match(block);
            if (!pct.Success)
            {
                continue;
            }

            var kind = WindowKindOf(block);
            if (kind is null)
            {
                continue;
            }

            var usedPct = double.Parse(pct.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            var resets = ParseReset(block, nowUtc);
            var dto = new ProviderUsageWindowDto(
                "claude", kind, Used: null, Limit: null, usedPct,
                resets, nowUtc,
                ProviderUsageSources.Cli, ProviderUsageConfidences.Measured);

            // several blocks can map to the same kind — highest pct wins.
            if (!windows.TryGetValue(kind, out var existing)
                || (existing.UsedPct ?? 0) < usedPct)
            {
                windows[kind] = dto;
            }
        }

        return [.. windows.Values];
    }

    /// <summary>Label → windowKind; null when the block isn't a quota window.</summary>
    private static string? WindowKindOf(string block)
    {
        var first = block.Split('\n', 2)[0];
        if (first.Contains("session", StringComparison.OrdinalIgnoreCase)
            || first.Contains("5", StringComparison.Ordinal)
            || first.Contains("hour", StringComparison.OrdinalIgnoreCase))
        {
            return ProviderWindowKinds.FiveHour;
        }

        if (first.Contains("day", StringComparison.OrdinalIgnoreCase)
            || first.Contains("daily", StringComparison.OrdinalIgnoreCase))
        {
            return ProviderWindowKinds.Daily;
        }

        if (first.Contains("week", StringComparison.OrdinalIgnoreCase))
        {
            return ProviderWindowKinds.Weekly;
        }

        if (first.Contains("rate", StringComparison.OrdinalIgnoreCase))
        {
            return ProviderWindowKinds.Rate;
        }

        return null;
    }

    private static DateTime? ParseReset(string block, DateTime nowUtc)
    {
        var within = ResetsIn.Match(block);
        if (within.Success
            && (within.Groups[1].Success || within.Groups[2].Success))
        {
            var hours = within.Groups[1].Success
                ? int.Parse(within.Groups[1].Value) : 0;
            var minutes = within.Groups[2].Success
                ? int.Parse(within.Groups[2].Value) : 0;
            return nowUtc.AddHours(hours).AddMinutes(minutes);
        }

        var at = ResetsAt.Match(block);
        if (at.Success)
        {
            var hour = int.Parse(at.Groups[1].Value);
            var minute = at.Groups[2].Success ? int.Parse(at.Groups[2].Value) : 0;
            var ampm = at.Groups[3].Value;
            if (ampm.Equals("pm", StringComparison.OrdinalIgnoreCase) && hour < 12)
            {
                hour += 12;
            }
            else if (ampm.Equals("am", StringComparison.OrdinalIgnoreCase) && hour == 12)
            {
                hour = 0;
            }

            // next occurrence of that wall-clock time — today or tomorrow.
            var candidate = new DateTime(
                nowUtc.Year, nowUtc.Month, nowUtc.Day, hour, minute, 0,
                DateTimeKind.Utc);
            return candidate <= nowUtc ? candidate.AddDays(1) : candidate;
        }

        return null;
    }
}
