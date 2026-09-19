using Taskboard.Harness;

namespace Taskboard.Domain.Entities.Harness;

/// <summary>
/// Persisted evidence of a verification run — summary plus the serialized
/// report payload for post-mortem / human review
/// (SPEC-20260919-harness-verification-loop RF-005).
/// </summary>
public sealed class VerificationReport : AggregateRoot<VerificationReportId>
{
    public string WorktreePath { get; private set; } = default!;
    public VerificationStatus Status { get; private set; }
    public bool IsSuccess { get; private set; }
    public double CoveragePercent { get; private set; }
    public int Attempts { get; private set; }
    public string DetailsJson { get; private set; } = default!;
    public DateTime CreatedAt { get; private set; }

    private VerificationReport()
    {
    }

    private VerificationReport(
        VerificationReportId id,
        string worktreePath,
        VerificationStatus status,
        bool isSuccess,
        double coveragePercent,
        int attempts,
        string detailsJson,
        DateTime now)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(worktreePath))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "WorktreePath cannot be empty.");
        }

        WorktreePath = worktreePath;
        Status = status;
        IsSuccess = isSuccess;
        CoveragePercent = coveragePercent;
        Attempts = attempts;
        DetailsJson = detailsJson;
        CreatedAt = now;
    }

    public static VerificationReport Create(
        VerificationReportId id,
        string worktreePath,
        VerificationStatus status,
        bool isSuccess,
        double coveragePercent,
        int attempts,
        string detailsJson,
        DateTime? now = null)
        => new(id, worktreePath, status, isSuccess, coveragePercent,
            attempts, detailsJson, now ?? DateTime.UtcNow);
}
