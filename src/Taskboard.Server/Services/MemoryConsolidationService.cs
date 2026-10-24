using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Server.Services;

/// <summary>
/// SPEC-20261024-p1-agent-reliability RF-F01/F02: managed job
/// <c>chat-memory-consolidation</c> — daily pass over
/// <see cref="ChatMemoryStore"/>: dedupes identical facts, expires facts
/// older than <c>Taskboard:Chat:Memory:MaxAgeDays</c> (default 90, 0 never
/// expires) and LRU-caps at <c>Taskboard:Chat:Memory:MaxFacts</c> (default
/// <see cref="ChatMemoryStore.MaxItems"/>). The {kept,deduped,expired,
/// evicted} summary lands in the cockpit job log; manual trigger comes from
/// the managed-job signal path.
/// </summary>
public sealed class MemoryConsolidationService : ManagedJobService
{
    public const string JobKey = "chat-memory-consolidation";
    public const string MaxAgeDaysKey = "Taskboard:Chat:Memory:MaxAgeDays";
    public const string MaxFactsKey = "Taskboard:Chat:Memory:MaxFacts";
    private const int DefaultMaxAgeDays = 90;

    private readonly ChatMemoryStore _store;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MemoryConsolidationService> _logger;

    public MemoryConsolidationService(
        ChatMemoryStore store,
        IConfiguration configuration,
        JobRegistry registry,
        ILogger<MemoryConsolidationService> logger)
        : base(registry, JobKey, logger)
    {
        _store = store;
        _configuration = configuration;
        _logger = logger;
    }

    protected override Task<string?> RunJobAsync(CancellationToken cancellationToken) =>
        ConsolidateOnce(cancellationToken);

    /// <summary>One consolidation pass — internal so the tick body is directly testable.</summary>
    internal Task<string?> ConsolidateOnce(CancellationToken cancellationToken)
    {
        var maxAgeDays = _configuration.GetValue(MaxAgeDaysKey, DefaultMaxAgeDays);
        var maxFacts = _configuration.GetValue(MaxFactsKey, ChatMemoryStore.MaxItems);
        var cutoff = maxAgeDays <= 0 ? DateTime.MinValue : DateTime.UtcNow.AddDays(-maxAgeDays);
        var report = _store.Consolidate(cutoff, maxFacts);
        if (report.Deduped + report.Expired + report.Evicted > 0)
        {
            _logger.LogInformation(
                "Chat memory consolidated: kept {Kept}, deduped {Deduped}, expired {Expired}, evicted {Evicted}",
                report.Kept, report.Deduped, report.Expired, report.Evicted);
        }

        return Task.FromResult<string?>(
            $"kept {report.Kept}, deduped {report.Deduped}, expired {report.Expired}, evicted {report.Evicted}");
    }
}
