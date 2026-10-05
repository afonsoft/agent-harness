using Microsoft.EntityFrameworkCore;
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
    private readonly ILogger<ChatRunRetentionService> _logger;

    public ChatRunRetentionService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        JobRegistry registry,
        ILogger<ChatRunRetentionService> logger)
        : base(registry, JobKey, logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
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
        }

        if (stale.Count > 0)
        {
            await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Chat runs: purged {Count} rows past {Days}d retention", stale.Count, days);
        }

        return stale.Count > 0 ? $"purged {stale.Count} rows past {days}d" : null;
    }
}
