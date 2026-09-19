using Taskboard.Agents;

namespace Taskboard.Dtos;

/// <summary>Per-source resolution/health result. SPEC-20260919-cli-db-reader RF-006.</summary>
public sealed record CliDbSourceStatusDto(
    AgentCliKind Kind,
    string SourceName,
    string Pattern,
    string? ResolvedPath,
    CliDbSourceStatus Status,
    string? Reason);

/// <summary>
/// Schema fingerprint of an external database: PRAGMA user_version,
/// application_id and the whitelisted table/column set. Compared against the
/// fingerprint recorded when an extractor was last verified — mismatch means
/// drift. SPEC-20260919-cli-db-reader RF-003.
/// </summary>
public sealed record CliDbSchemaFingerprint(
    long UserVersion,
    long ApplicationId,
    string TablesSignature);

/// <summary>
/// Normalized session metadata extracted from a vendor database. Message and
/// prompt content is never extracted (privacy boundary).
/// SPEC-20260919-cli-db-reader.
/// </summary>
public sealed record CliSessionRecord(
    string Source,
    string ExternalId,
    string? Title,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    int? MessageCount,
    string? ModelName,
    long? TokensInput,
    long? TokensOutput,
    long? TokensCached);

/// <summary>Normalized token/cost usage row when the vendor schema exposes it.</summary>
public sealed record CliUsageRecord(
    string Source,
    string ExternalId,
    string? SessionExternalId,
    DateTimeOffset RecordedAtUtc,
    string? ModelName,
    long? TokensInput,
    long? TokensOutput,
    long? TokensCached,
    decimal? CostUsd);

/// <summary>Result of one incremental extraction pass.</summary>
public sealed record CliExtractionResult(
    IReadOnlyList<CliSessionRecord> Sessions,
    IReadOnlyList<CliUsageRecord> Usage,
    string? NextCursor,
    CliDbSourceStatus Status,
    string? Reason);

/// <summary>
/// Read budget for external database access.
/// SPEC-20260919-cli-db-reader RF-005.
/// </summary>
public sealed record CliDbReadOptions(
    int RowLimit = 10_000,
    TimeSpan? CommandTimeout = null,
    long MaxFileSizeBytes = 512L * 1024 * 1024)
{
    public TimeSpan EffectiveCommandTimeout => CommandTimeout ?? TimeSpan.FromSeconds(5);
}
