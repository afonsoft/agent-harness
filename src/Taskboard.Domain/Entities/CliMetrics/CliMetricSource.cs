using Taskboard.Agents;
using Taskboard.CliMetrics;

namespace Taskboard.Domain.Entities.CliMetrics;

/// <summary>
/// One external CLI database file tracked for metrics ingestion — carries the
/// extractor watermark cursor and last-seen file state so syncs stay
/// incremental (SPEC-20260919-cli-metrics RF-002).
/// </summary>
public sealed class CliMetricSource : AggregateRoot<CliMetricSourceId>
{
    public AgentCliKind Kind { get; private set; }
    public string SourceName { get; private set; } = default!;
    public string RelativePath { get; private set; } = default!;
    public string? ResolvedPath { get; private set; }
    public CliDbSourceStatus Status { get; private set; }
    public string? SchemaFingerprint { get; private set; }
    public string? WatermarkCursor { get; private set; }
    public DateTime? FileModifiedUtc { get; private set; }
    public long? FileSizeBytes { get; private set; }
    public DateTime? LastSyncUtc { get; private set; }
    public string? LastError { get; private set; }
    public long RowCount { get; private set; }
    /// <summary>
    /// Extractor data version last applied — a declared-version bump clears
    /// the watermark so old rows re-extract once (SPEC-20260922 RF-003).
    /// </summary>
    public int ExtractorDataVersion { get; private set; } = 1;
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private CliMetricSource()
    {
    }

    private CliMetricSource(
        CliMetricSourceId id, AgentCliKind kind, string sourceName, string relativePath, DateTime now)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "SourceName cannot be empty.");
        }
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "RelativePath cannot be empty.");
        }

        Kind = kind;
        SourceName = sourceName;
        RelativePath = relativePath;
        Status = CliDbSourceStatus.Missing;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static CliMetricSource Register(
        AgentCliKind kind, string sourceName, string relativePath, DateTime now) =>
        new(CliMetricSourceId.NewGuid(), kind, sourceName, relativePath, now);

    /// <summary>Records the resolved file state seen this sync.</summary>
    public void RecordFileState(string resolvedPath, long mtimeUtcTicks, long sizeBytes, DateTime now)
    {
        ResolvedPath = resolvedPath;
        FileModifiedUtc = new DateTime(mtimeUtcTicks, DateTimeKind.Utc);
        FileSizeBytes = sizeBytes;
        UpdatedAt = now;
        IncrementVersion();
    }

    /// <summary>Whether the file's mtime+size differ from the last recorded state.</summary>
    public bool FileChangedSinceLastSync(string resolvedPath, long mtimeUtcTicks, long sizeBytes) =>
        !string.Equals(ResolvedPath, resolvedPath, StringComparison.Ordinal)
        || FileModifiedUtc?.Ticks != mtimeUtcTicks
        || FileSizeBytes != sizeBytes;

    public void MarkSync(CliDbSourceStatus status, string? watermarkCursor, long rowCount, DateTime now)
    {
        Status = status;
        WatermarkCursor = watermarkCursor;
        RowCount = rowCount;
        LastError = null;
        LastSyncUtc = now;
        UpdatedAt = now;
        IncrementVersion();
    }

    /// <summary>Clears the watermark cursor — the next sync re-reads the source from scratch.</summary>
    public void ResetWatermark(DateTime now)
    {
        WatermarkCursor = null;
        UpdatedAt = now;
        IncrementVersion();
    }

    /// <summary>
    /// Records the extractor data version applied by a successful sync; a bump
    /// clears the stored watermark cursor.
    /// </summary>
    public void RecordExtractorDataVersion(int version, DateTime now)
    {
        if (version == ExtractorDataVersion)
        {
            return;
        }
        ExtractorDataVersion = version;
        // Any version change invalidates the incremental cursor — the next
        // MarkSync call writes the fresh cursor over it anyway.
        ResetWatermark(now);
    }

    public void MarkError(string reason, DateTime now)
    {
        Status = CliDbSourceStatus.Error;
        LastError = reason;
        LastSyncUtc = now;
        UpdatedAt = now;
        IncrementVersion();
    }

    public void MarkMissing(DateTime now)
    {
        Status = CliDbSourceStatus.Missing;
        LastSyncUtc = now;
        UpdatedAt = now;
        IncrementVersion();
    }
}
