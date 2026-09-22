using Taskboard.Agents;
using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.CliDb;

/// <summary>
/// Whitelisted, append-style extractor for one CLI's databases. Executes only
/// queries against the <see cref="CliDbSource.WhitelistTables"/> declared in
/// <see cref="CliDatabaseMap"/> and never touches denied tables or
/// secret-named columns. SPEC-20260919-cli-db-reader RF-004.
/// </summary>
public interface ICliDbExtractor
{
    AgentCliKind Kind { get; }

    /// <summary>Sources this extractor actually reads (subset of <see cref="CliDatabaseMap.SourcesFor"/>).</summary>
    IReadOnlyList<CliDbSource> Sources { get; }

    /// <summary>Fingerprint recorded when this extractor was last verified against the vendor schema.</summary>
    CliDbSchemaFingerprint ExpectedFingerprint { get; }

    /// <summary>
    /// Extractor data schema version. A bump over the version stored on the
    /// source row clears the watermark cursor and forces one full re-extract
    /// (upserts stay idempotent). SPEC-20260922-finops-cli-usage-breakdown RF-003.
    /// </summary>
    int DataVersion { get; }

    /// <summary>
    /// Incremental extraction from an opaque watermark cursor (extractor-defined:
    /// rowid or timestamp). Same cursor → same results (deterministic ordering).
    /// </summary>
    Task<CliExtractionResult> ExtractSinceAsync(string? cursor, CancellationToken cancellationToken = default);
}
