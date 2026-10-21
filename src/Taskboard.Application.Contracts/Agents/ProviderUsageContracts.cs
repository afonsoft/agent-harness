namespace Taskboard.Application.Contracts.Agents;

/// <summary>SPEC-20261020-provider-quota-usage RF-001: wire shape of a quota
/// window — produced by collectors, persisted by the service.</summary>
public sealed record ProviderUsageWindowDto(
    string Provider,
    /// <summary>fiveHour | daily | weekly | rate.</summary>
    string WindowKind,
    long? Used,
    long? Limit,
    /// <summary>0–100 percent used — badge/warn signal.</summary>
    double? UsedPct,
    DateTime? ResetsAtUtc,
    DateTime SampledAtUtc,
    /// <summary>api | cli | local_estimate.</summary>
    string Source,
    /// <summary>measured | estimated.</summary>
    string Confidence,
    string? RawJson = null,
    int WarnLevel = 0);

/// <summary>RF-005: roster entry — one provider with its windows; stale when
/// the freshest sample is older than 15min.</summary>
public sealed record ProviderUsageRosterEntryDto(
    string Provider,
    IReadOnlyList<ProviderUsageWindowDto> Windows,
    DateTime? SampledAtUtc,
    bool Stale);

/// <summary>Persistence boundary — the entity stays inside the EF layer.</summary>
public interface IProviderUsageRepository
{
    Task<IReadOnlyList<ProviderUsageWindowDto>> ListAsync(
        string? provider = null, CancellationToken ct = default);

    Task<ProviderUsageWindowDto?> GetAsync(
        string provider, string windowKind, CancellationToken ct = default);

    /// <summary>Creates or refreshes the (provider, windowKind) row;
    /// WarnLevel survives the upsert. Returns the stored row.</summary>
    Task<ProviderUsageWindowDto> UpsertAsync(
        ProviderUsageWindowDto sample, CancellationToken ct = default);

    Task SetWarnLevelAsync(
        string provider, string windowKind, int level,
        CancellationToken ct = default);
}

/// <summary>
/// RF-002: one quota collector per provider — returns the windows it could
/// measure, empty when the provider is offline/unauthenticated. Never throws
/// for provider errors; the job marks staleness via SampledAtUtc age.
/// </summary>
public interface IProviderUsageCollector
{
    /// <summary>Provider slug matching the registry — "claude", "codex", ...</summary>
    string Provider { get; }

    Task<IReadOnlyList<ProviderUsageWindowDto>> CollectAsync(
        CancellationToken ct = default);
}

/// <summary>
/// SPEC-20261020-provider-quota-usage: collect → upsert → warn pipeline.
/// Singleton resolving scoped collaborators per operation.
/// </summary>
public interface IProviderUsageService
{
    /// <summary>One collect tick — every registered collector, tolerant of
    /// individual failures. Returns windows upserted.</summary>
    Task<int> CollectOnceAsync(string userId, CancellationToken ct = default);

    /// <summary>RF-005: roster grouped by provider; n/a windows never
    /// persist; stale when freshest sample >15min.</summary>
    Task<IReadOnlyList<ProviderUsageRosterEntryDto>> GetRosterAsync(
        CancellationToken ct = default);

    /// <summary>RF-005: one provider's detail — null when never sampled.</summary>
    Task<ProviderUsageRosterEntryDto?> GetProviderAsync(
        string provider, CancellationToken ct = default);
}
