using Microsoft.EntityFrameworkCore;
using Taskboard.Agents;
using Taskboard.Application.Contracts.CliMetrics;
using Taskboard.Dtos;
using Taskboard.EntityFrameworkCore.Data;

namespace Taskboard.EntityFrameworkCore.CliMetrics;

/// <inheritdoc cref="ICliUsageMetricsProvider"/>
public sealed class EfCoreCliUsageMetricsProvider : ICliUsageMetricsProvider
{
    private readonly TaskboardDbContext _context;

    public EfCoreCliUsageMetricsProvider(TaskboardDbContext context)
    {
        _context = context;
    }

    public Task<IReadOnlyList<CliUsageAggregateDto>> GetUsageAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
        QueryAsync(null, fromUtc, toUtc, cancellationToken);

    public Task<IReadOnlyList<CliUsageAggregateDto>> GetUsageAsync(
        AgentCliKind kind, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
        QueryAsync(kind, fromUtc, toUtc, cancellationToken);

    private async Task<IReadOnlyList<CliUsageAggregateDto>> QueryAsync(
        AgentCliKind? kind, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        var query = _context.CliSessionMetrics
            .AsNoTracking()
            .Where(s => s.StartedAtUtc >= fromUtc && s.StartedAtUtc < toUtc);
        if (kind is not null)
        {
            query = query.Where(s => s.Kind == kind);
        }

        var rows = await query
            .Select(s => new { s.Kind, s.ModelName, s.MessageCount, s.TokensInput, s.TokensOutput, s.TokensCached })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(r => (r.Kind, r.ModelName))
            .OrderBy(g => g.Key.Kind).ThenBy(g => g.Key.ModelName)
            .Select(g => new CliUsageAggregateDto(
                g.Key.Kind.ToString(), g.Key.ModelName,
                g.Count(),
                g.Sum(x => (long)(x.MessageCount ?? 0)),
                g.Sum(x => x.TokensInput ?? 0),
                g.Sum(x => x.TokensOutput ?? 0),
                g.Sum(x => x.TokensCached ?? 0)))
            .ToList();
    }
}
