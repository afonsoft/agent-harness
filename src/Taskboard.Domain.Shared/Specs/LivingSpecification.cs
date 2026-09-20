namespace Taskboard.Specs;

/// <summary>
/// In-memory projection of a <c>.specs/SPEC-*.md</c> file — the file on disk is
/// the source of truth; this object is rebuilt on every scan
/// (SPEC-20260919-ade-living-specs §2). Lives in Domain.Shared so the parser in
/// Integrations and the contracts layer can both see it.
/// </summary>
public sealed class LivingSpecification
{
    public LivingSpecification(
        string id,
        string filePath,
        string title,
        string? type,
        string? stack,
        string? branch,
        string? ticket,
        SpecStatus status,
        string? rawStatus,
        DateOnly? date,
        IReadOnlyList<SpecRequirement> requirements,
        IReadOnlyList<string> acceptanceCriteria,
        IReadOnlyList<SpecTask> tasks,
        IReadOnlyList<string> referencedFiles,
        IReadOnlyList<SpecLintWarning> warnings)
    {
        Id = id;
        FilePath = filePath;
        Title = title;
        Type = type;
        Stack = stack;
        Branch = branch;
        Ticket = ticket;
        Status = status;
        RawStatus = rawStatus;
        Date = date;
        Requirements = requirements;
        AcceptanceCriteria = acceptanceCriteria;
        Tasks = tasks;
        ReferencedFiles = referencedFiles;
        Warnings = warnings;
    }

    /// <summary>Filename without extension, e.g. <c>SPEC-20260919-ade-cockpit-hitl</c>.</summary>
    public string Id { get; }

    public string FilePath { get; }

    public string Title { get; }

    public string? Type { get; }

    public string? Stack { get; }

    public string? Branch { get; }

    public string? Ticket { get; }

    public SpecStatus Status { get; }

    /// <summary>Original status cell text, e.g. <c>Done — merged via PR #94</c>.</summary>
    public string? RawStatus { get; }

    public DateOnly? Date { get; }

    public IReadOnlyList<SpecRequirement> Requirements { get; }

    /// <summary>Acceptance-criterion lines (BDD checkboxes) from section 6.</summary>
    public IReadOnlyList<string> AcceptanceCriteria { get; }

    public IReadOnlyList<SpecTask> Tasks { get; }

    /// <summary>Repo-relative paths listed under "Files to create or modify" — input for drift detection.</summary>
    public IReadOnlyList<string> ReferencedFiles { get; }

    public IReadOnlyList<SpecLintWarning> Warnings { get; }
}

public sealed record SpecRequirement(string Code, string Title);

public sealed record SpecTask(string Title, bool Done);
