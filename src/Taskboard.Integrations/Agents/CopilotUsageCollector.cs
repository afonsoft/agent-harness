using System.Text.Json;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Integrations.Execution;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// SPEC-20261020-provider-quota-usage RF-002/P2: GitHub Copilot quota —
/// <c>gh api copilot_internal/user</c> via <see cref="IProcessRunner"/>
/// (gh handles auth; the token never enters this process). Parses
/// <c>quota_snapshots.*.percent_remaining</c> → monthly windows
/// (premium-request quota resets monthly). Empty when gh/the endpoint or
/// a Copilot seat is unavailable.
/// </summary>
public sealed class CopilotUsageCollector : IProviderUsageCollector
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly IProcessRunner _runner;
    private readonly string _workdir;

    public CopilotUsageCollector(IProcessRunner runner, string? workdir = null)
    {
        _runner = runner;
        _workdir = workdir ?? Directory.GetCurrentDirectory();
    }

    public string Provider => "copilot";

    public async Task<IReadOnlyList<ProviderUsageWindowDto>> CollectAsync(
        CancellationToken ct = default)
    {
        var result = await _runner.RunAsync(
            "gh", _workdir, ["api", "copilot_internal/user"], Timeout, ct);
        if (result.TimedOut || result.ExitCode != 0)
        {
            return [];
        }

        return CopilotUsageParser.Parse(result.StdOut, DateTime.UtcNow);
    }
}

/// <summary>
/// Parses <c>copilot_internal/user</c> JSON: <c>quota_snapshots</c> entries
/// with <c>percent_remaining</c> → UsedPct = 100 − remaining,
/// <c>reset_date</c> → ResetsAtUtc, <c>unlimited</c> entries are skipped.
/// </summary>
internal static class CopilotUsageParser
{
    public static IReadOnlyList<ProviderUsageWindowDto> Parse(
        string json, DateTime nowUtc)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("quota_snapshots", out var quotas)
                || quotas.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var windows = new List<ProviderUsageWindowDto>();
            foreach (var snapshot in quotas.EnumerateObject())
            {
                var q = snapshot.Value;
                if (q.ValueKind != JsonValueKind.Object
                    || (q.TryGetProperty("unlimited", out var u)
                        && u.ValueKind == JsonValueKind.True)
                    || !q.TryGetProperty("percent_remaining", out var rem)
                    || rem.ValueKind != JsonValueKind.Number
                    || !rem.TryGetDouble(out var remaining))
                {
                    continue;
                }

                var resets = q.TryGetProperty("reset_date", out var rd)
                    && rd.ValueKind == JsonValueKind.String
                    && DateTime.TryParse(rd.GetString(),
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal
                            | System.Globalization.DateTimeStyles.AssumeUniversal,
                        out var reset)
                        ? reset
                        : (DateTime?)null;

                windows.Add(new ProviderUsageWindowDto(
                    "copilot", ProviderWindowKinds.Monthly,
                    Used: null, Limit: null,
                    Math.Clamp(100 - remaining, 0, 100),
                    resets, nowUtc,
                    ProviderUsageSources.Api,
                    ProviderUsageConfidences.Measured));
            }

            // highest quota pressure wins when several snapshots exist.
            return windows.Count == 0
                ? []
                : [windows.OrderByDescending(w => w.UsedPct ?? 0).First()];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
