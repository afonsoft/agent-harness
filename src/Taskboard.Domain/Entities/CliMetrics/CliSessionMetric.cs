using Taskboard.Agents;
using Taskboard.CliMetrics;

namespace Taskboard.Domain.Entities.CliMetrics;

/// <summary>
/// Normalized session row ingested from an external CLI database — metadata
/// only, never message/prompt content (privacy boundary). Deduped by
/// (CliMetricSourceId, ExternalId); mutable fields update on re-ingest.
/// SPEC-20260919-cli-metrics RF-003.
/// </summary>
public sealed class CliSessionMetric : AggregateRoot<CliSessionMetricId>
{
    public CliMetricSourceId SourceId { get; private set; } = default!;
    public AgentCliKind Kind { get; private set; }
    public string ExternalId { get; private set; } = default!;
    public string? Title { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? EndedAtUtc { get; private set; }
    public int? MessageCount { get; private set; }
    public string? ModelName { get; private set; }
    public long? TokensInput { get; private set; }
    public long? TokensOutput { get; private set; }
    public long? TokensCached { get; private set; }
    /// <summary>Projected USD cost — set by the FinOps aggregation job (null until costed).</summary>
    public decimal? CostUsd { get; private set; }
    /// <summary>
    /// True when the extractor produced token counts via estimation (chars/4)
    /// or the vendor schema lacks real usage columns.
    /// SPEC-20260922-finops-dashboard-detail RF-006.
    /// </summary>
    public bool TokensEstimated { get; private set; }
    public DateTime IngestedAtUtc { get; private set; }

    private CliSessionMetric()
    {
    }

    private CliSessionMetric(
        CliSessionMetricId id, CliMetricSourceId sourceId, AgentCliKind kind, string externalId,
        string? title, DateTime startedAtUtc, DateTime? endedAtUtc, int? messageCount,
        string? modelName, long? tokensInput, long? tokensOutput, long? tokensCached,
        bool tokensEstimated, DateTime ingestedAtUtc)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "ExternalId cannot be empty.");
        }

        SourceId = sourceId;
        Kind = kind;
        ExternalId = externalId;
        Title = title;
        // Clock skew: sessions stamped in the future clamp to now (SPEC edge case).
        StartedAtUtc = startedAtUtc > ingestedAtUtc ? ingestedAtUtc : startedAtUtc;
        EndedAtUtc = endedAtUtc;
        MessageCount = messageCount;
        ModelName = modelName;
        TokensInput = tokensInput;
        TokensOutput = tokensOutput;
        TokensCached = tokensCached;
        TokensEstimated = tokensEstimated;
        IngestedAtUtc = ingestedAtUtc;
    }

    public static CliSessionMetric Create(
        CliMetricSourceId sourceId, AgentCliKind kind, string externalId, string? title,
        DateTime startedAtUtc, DateTime? endedAtUtc, int? messageCount, string? modelName,
        long? tokensInput, long? tokensOutput, long? tokensCached, DateTime now,
        bool tokensEstimated = false) =>
        new(CliSessionMetricId.NewGuid(), sourceId, kind, externalId, title, startedAtUtc,
            endedAtUtc, messageCount, modelName, tokensInput, tokensOutput, tokensCached,
            tokensEstimated, now);

    /// <summary>Re-ingest path — mutable fields only; identity never changes.</summary>
    public void Update(
        string? title, DateTime? endedAtUtc, int? messageCount, string? modelName,
        long? tokensInput, long? tokensOutput, long? tokensCached, DateTime now,
        bool tokensEstimated = false)
    {
        Title = title;
        EndedAtUtc = endedAtUtc;
        MessageCount = messageCount;
        ModelName = modelName;
        TokensInput = tokensInput;
        TokensOutput = tokensOutput;
        TokensCached = tokensCached;
        TokensEstimated = tokensEstimated;
        CostUsd = null; // fresh token data — FinOps aggregation re-costs on next tick
        IngestedAtUtc = now;
        IncrementVersion();
    }

    /// <summary>Records the projected cost computed by the FinOps aggregation job.</summary>
    public void SetCost(decimal costUsd)
    {
        CostUsd = costUsd;
        IncrementVersion();
    }
}
