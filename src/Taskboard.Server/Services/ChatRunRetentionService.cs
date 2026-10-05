using Microsoft.EntityFrameworkCore;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Domain.Entities.Chat;
using Taskboard.Repositories;
using Taskboard.ValueObjects;

namespace Taskboard.Server.Services;

/// <summary>
/// SPEC-20261005-chat-background-resume RF-001: retention for finished
/// <see cref="ChatRun"/> rows — deletes terminal runs older than
/// <c>Taskboard:Chat:Runs:RetentionDays</c> (default 30, 0 disables).
/// Managed job <c>chat-run-retention</c>, default every 6 hours.
/// </summary>
public sealed class ChatRunRetentionService : ManagedJobService
{
    public const string JobKey = "chat-run-retention";
    public const string RetentionDaysKey = "Taskboard:Chat:Runs:RetentionDays";
    private const int DefaultRetentionDays = 30;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ISpillStore _spillStore;
    private readonly ILogger<ChatRunRetentionService> _logger;

    public ChatRunRetentionService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ISpillStore spillStore,
        JobRegistry registry,
        ILogger<ChatRunRetentionService> logger)
        : base(registry, JobKey, logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _spillStore = spillStore;
        _logger = logger;
    }

    protected override async Task<string?> RunJobAsync(CancellationToken cancellationToken)
    {
        var days = _configuration.GetValue(RetentionDaysKey, DefaultRetentionDays);
        if (days <= 0)
        {
            return "retention disabled (RetentionDays <= 0)";
        }

        var cutoff = DateTime.UtcNow.AddDays(-days);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<ChatRun>>();
        var stale = await repository.Query
            .Where(r => r.FinishedAt != null && r.FinishedAt < cutoff)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var run in stale)
        {
            await repository.DeleteAsync(run, cancellationToken).ConfigureAwait(false);
            // SPEC-20261005-chat-context-management RF-008: the run's spilled
            // outputs go with the row.
            _spillStore.DeleteRunDir(run.Id.Value);
        }

        if (stale.Count > 0)
        {
            await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Chat runs: purged {Count} rows past {Days}d retention", stale.Count, days);
        }

        // RF-008: orphan sweep — spill dirs whose run row vanished by other
        // means (manual delete, DB reset). Runs at every job tick including
        // the boot-time tick the managed-job service performs on start.
        var alive = (await repository.Query
            .Select(r => r.Id.Value)
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);
        var swept = _spillStore.SweepOrphans(alive);
        if (swept > 0)
        {
            _logger.LogInformation("Chat spills: swept {Count} orphan run dirs", swept);
        }

        return stale.Count > 0 || swept > 0
            ? $"purged {stale.Count} rows past {days}d, swept {swept} orphan spill dirs"
            : null;
    }
}
