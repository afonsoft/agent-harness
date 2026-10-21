namespace Taskboard.Domain.Agents;

/// <summary>
/// SPEC-20261020-provider-quota-usage RF-001: latest sampled state of one
/// provider quota window (5h/daily/weekly/rate). Upserted per
/// (Provider, WindowKind) on each collect tick; <see cref="WarnLevel"/>
/// persists the warn hysteresis across restarts.
/// </summary>
public sealed class ProviderUsageWindow : Entity<Guid>
{
    private ProviderUsageWindow(Guid id)
        : base(id)
    {
    }

    public string Provider { get; private set; } = string.Empty;
    /// <summary>fiveHour | daily | weekly | rate.</summary>
    public string WindowKind { get; private set; } = string.Empty;
    /// <summary>Units consumed in the window when the source reports them.</summary>
    public long? Used { get; private set; }
    /// <summary>Window limit when the source reports it.</summary>
    public long? Limit { get; private set; }
    /// <summary>0–100 percent used — the badge/warn signal.</summary>
    public double? UsedPct { get; private set; }
    public DateTime? ResetsAtUtc { get; private set; }
    public DateTime SampledAtUtc { get; private set; }
    /// <summary>api | cli | local_estimate.</summary>
    public string Source { get; private set; } = string.Empty;
    /// <summary>measured | estimated.</summary>
    public string Confidence { get; private set; } = string.Empty;
    /// <summary>≤2KB sanitized raw payload for debugging; never tokens/keys.</summary>
    public string? RawJson { get; private set; }
    /// <summary>Warn hysteresis: 0 none, 1 quota_warning emitted,
    /// 2 quota_exhausted emitted. Re-arms below 75%.</summary>
    public int WarnLevel { get; private set; }
    /// <summary>P2 RF-008: JSON array of the last ≤10 UsedPct samples.</summary>
    public string? RecentPctsJson { get; private set; }

    public static ProviderUsageWindow Create(
        string provider, string windowKind,
        long? used, long? limit, double? usedPct,
        DateTime? resetsAtUtc, DateTime sampledAtUtc,
        string source, string confidence, string? rawJson)
    {
        var window = new ProviderUsageWindow(Guid.NewGuid())
        {
            Provider = provider,
            WindowKind = windowKind,
            Used = used,
            Limit = limit,
            UsedPct = usedPct,
            ResetsAtUtc = resetsAtUtc,
            SampledAtUtc = sampledAtUtc,
            Source = source,
            Confidence = confidence,
            RawJson = rawJson,
            WarnLevel = 0,
        };
        window.AppendPct(usedPct);
        return window;
    }

    /// <summary>Refresh from the latest sample; WarnLevel survives.</summary>
    public void Apply(
        long? used, long? limit, double? usedPct,
        DateTime? resetsAtUtc, DateTime sampledAtUtc,
        string source, string confidence, string? rawJson)
    {
        Used = used;
        Limit = limit;
        UsedPct = usedPct;
        ResetsAtUtc = resetsAtUtc;
        SampledAtUtc = sampledAtUtc;
        Source = source;
        Confidence = confidence;
        RawJson = rawJson;
        AppendPct(usedPct);
    }

    /// <summary>Append a sampled pct, keeping the last 10 (sparkline).</summary>
    private void AppendPct(double? pct)
    {
        if (pct is not { } value)
        {
            return;
        }

        var recent = DeserializeRecent();
        recent.Add(value);
        if (recent.Count > 10)
        {
            recent.RemoveRange(0, recent.Count - 10);
        }

        RecentPctsJson = System.Text.Json.JsonSerializer.Serialize(recent);
    }

    /// <summary>Tolerant parse of <see cref="RecentPctsJson"/> — garbage → empty.</summary>
    public List<double> DeserializeRecent()
    {
        if (RecentPctsJson is not { Length: > 0 } raw)
        {
            return [];
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<double>>(raw) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    public void MarkWarned(int level) => WarnLevel = level;
    public void Rearm() => WarnLevel = 0;
}
