using Taskboard.Agents;

namespace Taskboard.Domain.Entities.Harness;

/// <summary>
/// Immutable cost metric recorded when an agent run or pipeline stage reports
/// token usage (SPEC-20260919-ade-observability-finops RF-001/RF-002).
/// </summary>
public sealed class RunCostMetric : Entity<Guid>
{
    /// <summary>Run identifier — AgentRun GUID ("N") or pipeline execution id.</summary>
    public string RunId { get; private set; } = string.Empty;

    /// <summary>Pipeline stage key when the metric belongs to a stage.</summary>
    public string? StageKey { get; private set; }

    public AgentType AgentType { get; private set; }

    /// <summary>Resolved model name; null when the CLI did not report one.</summary>
    public string? ModelName { get; private set; }

    public long InputTokens { get; private set; }

    public long OutputTokens { get; private set; }

    public long CacheWriteTokens { get; private set; }

    public long CacheReadTokens { get; private set; }

    /// <summary>Computed cost in USD (exact decimal math, RF-002).</summary>
    public decimal CostUsd { get; private set; }

    /// <summary>Budget cap in effect when this metric was recorded.</summary>
    public decimal? BudgetCapUsd { get; private set; }

    /// <summary>UTC timestamp — DateTime (SQLite-friendly) like PipelineExecution.</summary>
    public DateTime RecordedAtUtc { get; private set; }

    public long TotalTokens => InputTokens + OutputTokens + CacheWriteTokens + CacheReadTokens;

    private RunCostMetric()
    {
    }

    public RunCostMetric(
        Guid id,
        string runId,
        AgentType agentType,
        string? modelName,
        long inputTokens,
        long outputTokens,
        long cacheWriteTokens,
        long cacheReadTokens,
        decimal costUsd,
        DateTime recordedAtUtc,
        string? stageKey = null,
        decimal? budgetCapUsd = null)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        RunId = runId;
        StageKey = stageKey;
        AgentType = agentType;
        ModelName = modelName;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CacheWriteTokens = cacheWriteTokens;
        CacheReadTokens = cacheReadTokens;
        CostUsd = costUsd;
        BudgetCapUsd = budgetCapUsd;
        RecordedAtUtc = recordedAtUtc;
    }
}
