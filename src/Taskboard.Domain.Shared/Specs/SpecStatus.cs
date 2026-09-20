namespace Taskboard.Specs;

/// <summary>
/// Lifecycle of a living specification (SPEC-20260919-ade-living-specs §2).
/// Persisted as the <c>Status</c> cell of the spec's metadata table — raw values
/// such as <c>Implemented</c>, <c>Completed</c> or annotated
/// <c>Done — merged via PR #94</c> are normalized to <see cref="Done"/>.
/// </summary>
public enum SpecStatus
{
    Draft = 0,
    Approved = 1,
    InImplementation = 2,
    Done = 3,
    Deprecated = 4
}
