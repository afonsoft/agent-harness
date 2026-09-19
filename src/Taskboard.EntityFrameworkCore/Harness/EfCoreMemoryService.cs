using Microsoft.EntityFrameworkCore;
using Taskboard;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Domain.Entities.Harness;
using Taskboard.Dtos;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.Harness;

namespace Taskboard.EntityFrameworkCore.Harness;

/// <summary>
/// SQLite-backed <see cref="IMemoryService"/> — lexical keyword/tag search
/// scoped per repository (SPEC-20260919-harness-context-memory RF-004).
/// </summary>
public sealed class EfCoreMemoryService : IMemoryService
{
    private readonly TaskboardDbContext _context;

    public EfCoreMemoryService(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task<ProjectMemoryItemDto> AddMemoryAsync(
        string repositoryFullName,
        string topic,
        string content,
        IReadOnlyList<string>? tags = null,
        MemoryType type = MemoryType.Fact,
        CancellationToken cancellationToken = default)
    {
        var entity = ProjectMemoryItem.Create(
            ProjectMemoryItemId.NewGuid(),
            repositoryFullName,
            topic,
            content,
            type,
            tags);

        await _context.ProjectMemoryItems.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<IReadOnlyList<ProjectMemoryItemDto>> SearchAsync(
        string repositoryFullName,
        string query,
        int take = 10,
        CancellationToken cancellationToken = default)
    {
        var terms = (query ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .ToList();

        var scoped = await _context.ProjectMemoryItems
            .Where(m => m.RepositoryFullName == repositoryFullName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ranked = scoped
            .Select(m => (Item: m, Score: Score(m, terms)))
            .Where(x => terms.Count == 0 || x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Item.CreatedAt)
            .Take(Math.Clamp(take, 1, 100))
            .Select(x => ToDto(x.Item))
            .ToList();

        return ranked;
    }

    public async Task<IReadOnlyList<ProjectMemoryItemDto>> ListAsync(
        string repositoryFullName,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var items = await _context.ProjectMemoryItems
            .Where(m => m.RepositoryFullName == repositoryFullName)
            .OrderByDescending(m => m.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return items.Select(ToDto).ToList();
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.ProjectMemoryItems
            .FirstOrDefaultAsync(m => m.Id == ProjectMemoryItemId.From(id), cancellationToken)
            .ConfigureAwait(false)
            ?? throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, $"No memory item '{id}'.");

        _context.ProjectMemoryItems.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static int Score(ProjectMemoryItem item, IReadOnlyList<string> terms)
    {
        var topic = item.Topic.ToLowerInvariant();
        var content = item.Content.ToLowerInvariant();
        var score = 0;
        foreach (var term in terms)
        {
            if (item.Tags.Contains(term))
            {
                score += 4;
            }

            if (topic.Contains(term, StringComparison.Ordinal))
            {
                score += 2;
            }

            if (content.Contains(term, StringComparison.Ordinal))
            {
                score += 1;
            }
        }

        return score;
    }

    private static ProjectMemoryItemDto ToDto(ProjectMemoryItem m)
        => new(
            m.Id.Value,
            m.RepositoryFullName,
            m.Topic,
            m.Content,
            m.Type.ToString(),
            m.Tags,
            m.CreatedAt,
            m.UpdatedAt,
            m.Version);
}
