using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Domain.Entities.Harness;
using Taskboard.Dtos;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.Harness;

namespace Taskboard.EntityFrameworkCore.Harness;

/// <inheritdoc cref="IVerificationReportRepository"/>
public sealed class EfCoreVerificationReportRepository : IVerificationReportRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly TaskboardDbContext _context;

    public EfCoreVerificationReportRepository(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task SaveAsync(
        string worktreePath,
        VerificationReportDto report,
        int attempts,
        CancellationToken cancellationToken = default)
    {
        var status = Enum.TryParse<VerificationStatus>(report.Status, out var parsed)
            ? parsed
            : VerificationStatus.EscalatedToHuman;

        var entity = VerificationReport.Create(
            VerificationReportId.NewGuid(),
            worktreePath,
            status,
            report.IsSuccess,
            report.CoveragePercent,
            attempts,
            JsonSerializer.Serialize(report, JsonOptions));

        await _context.VerificationReports.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
