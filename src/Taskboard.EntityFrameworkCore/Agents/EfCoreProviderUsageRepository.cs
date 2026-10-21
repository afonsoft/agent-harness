using Microsoft.EntityFrameworkCore;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Domain.Agents;
using Taskboard.EntityFrameworkCore.Data;

namespace Taskboard.EntityFrameworkCore.Agents;

/// <summary>SPEC-20261020-provider-quota-usage RF-001: EF persistence.</summary>
public sealed class EfCoreProviderUsageRepository : IProviderUsageRepository
{
    private readonly TaskboardDbContext _context;

    public EfCoreProviderUsageRepository(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ProviderUsageWindowDto>> ListAsync(
        string? provider = null, CancellationToken ct = default) =>
        await _context.ProviderUsageWindows
            .Where(w => provider == null || w.Provider == provider)
            .OrderBy(w => w.Provider).ThenBy(w => w.WindowKind)
            .Select(w => ToDto(w))
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<ProviderUsageWindowDto?> GetAsync(
        string provider, string windowKind, CancellationToken ct = default) =>
        await _context.ProviderUsageWindows
            .Where(w => w.Provider == provider && w.WindowKind == windowKind)
            .Select(w => ToDto(w))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public async Task<ProviderUsageWindowDto> UpsertAsync(
        ProviderUsageWindowDto sample, CancellationToken ct = default)
    {
        var existing = await _context.ProviderUsageWindows
            .FirstOrDefaultAsync(
                w => w.Provider == sample.Provider && w.WindowKind == sample.WindowKind,
                ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            existing = ProviderUsageWindow.Create(
                sample.Provider, sample.WindowKind,
                sample.Used, sample.Limit, sample.UsedPct,
                sample.ResetsAtUtc, sample.SampledAtUtc,
                sample.Source, sample.Confidence, sample.RawJson);
            _context.ProviderUsageWindows.Add(existing);
        }
        else
        {
            existing.Apply(
                sample.Used, sample.Limit, sample.UsedPct,
                sample.ResetsAtUtc, sample.SampledAtUtc,
                sample.Source, sample.Confidence, sample.RawJson);
        }

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(existing);
    }

    public async Task SetWarnLevelAsync(
        string provider, string windowKind, int level,
        CancellationToken ct = default)
    {
        var existing = await _context.ProviderUsageWindows
            .FirstOrDefaultAsync(
                w => w.Provider == provider && w.WindowKind == windowKind, ct)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }

        if (level > 0)
        {
            existing.MarkWarned(level);
        }
        else
        {
            existing.Rearm();
        }

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static ProviderUsageWindowDto ToDto(ProviderUsageWindow w) =>
        new(w.Provider, w.WindowKind, w.Used, w.Limit, w.UsedPct,
            w.ResetsAtUtc, w.SampledAtUtc, w.Source, w.Confidence,
            w.RawJson, w.WarnLevel);
}
