using System.Text.Json;
using Taskboard.Agents;
using Taskboard.CliMetrics;

namespace Taskboard.Domain.Entities.CliMetrics;

/// <summary>
/// Per-(Kind, Day) rollup of session metrics — retained indefinitely while raw
/// session rows age out (SPEC-20260919-cli-metrics RF-004/RF-008).
/// </summary>
public sealed class CliDailyUsageAggregate : AggregateRoot<CliDailyUsageAggregateId>
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public AgentCliKind Kind { get; private set; }
    /// <summary>ISO <c>yyyy-MM-dd</c> day bucket (UTC).</summary>
    public string Day { get; private set; } = default!;
    public int SessionsCount { get; private set; }
    public int MessagesCount { get; private set; }
    public long TokensInput { get; private set; }
    public long TokensOutput { get; private set; }
    public long TokensCached { get; private set; }
    public string ModelsJson { get; private set; } = "[]";
    public DateTime UpdatedAt { get; private set; }

    private List<string>? _models;

    private CliDailyUsageAggregate()
    {
    }

    private CliDailyUsageAggregate(
        CliDailyUsageAggregateId id, AgentCliKind kind, string day, DateTime now)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(day))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Day cannot be empty.");
        }

        Kind = kind;
        Day = day;
        UpdatedAt = now;
    }

    public static CliDailyUsageAggregate Register(AgentCliKind kind, string day, DateTime now) =>
        new(CliDailyUsageAggregateId.NewGuid(), kind, day, now);

    public IReadOnlyList<string> ModelsUsed =>
        _models ??= JsonSerializer.Deserialize<List<string>>(ModelsJson, JsonOptions) ?? [];

    /// <summary>Zeroes the counters so the bucket can be rebuilt from raw rows.</summary>
    public void Reset(DateTime now)
    {
        SessionsCount = 0;
        MessagesCount = 0;
        TokensInput = 0;
        TokensOutput = 0;
        TokensCached = 0;
        _models = [];
        ModelsJson = "[]";
        UpdatedAt = now;
        IncrementVersion();
    }

    public void Add(
        int sessions, int messages, long tokensIn, long tokensOut, long tokensCached,
        string? modelName, DateTime now)
    {
        SessionsCount += sessions;
        MessagesCount += messages;
        TokensInput += tokensIn;
        TokensOutput += tokensOut;
        TokensCached += tokensCached;

        if (!string.IsNullOrEmpty(modelName))
        {
            var models = _models ??= [.. ModelsUsed];
            if (!models.Contains(modelName, StringComparer.Ordinal))
            {
                models.Add(modelName);
                models.Sort(StringComparer.Ordinal);
                ModelsJson = JsonSerializer.Serialize(models, JsonOptions);
            }
        }

        UpdatedAt = now;
        IncrementVersion();
    }
}
