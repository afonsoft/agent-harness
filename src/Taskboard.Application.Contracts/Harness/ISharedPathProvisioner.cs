namespace Taskboard.Application.Contracts.Harness;

/// <summary>
/// SPEC-20261020-worktree-shared-context RF-001/RF-002: one resolved shared
/// path and whether it was materialized in the worktree.
/// </summary>
public sealed record SharedPathEntryDto(
    string Path,
    string Mode,
    string Source,
    bool Applied,
    string? Note);

/// <summary>
/// Copies/symlinks declared gitignored paths from the source repo into a
/// freshly created worktree (<c>git worktree add</c> leaves them empty —
/// workers otherwise reinstall deps or fail without .env context). Tolerant:
/// individual path failures produce skipped entries, never throw.
/// </summary>
public interface ISharedPathProvisioner
{
    /// <summary>Resolve declaration sources and materialize each entry into
    /// <paramref name="worktreePath"/>. Returns the full resolution list —
    /// callers read <c>Applied</c> for the <c>sharedPathsApplied</c> preamble.</summary>
    Task<IReadOnlyList<SharedPathEntryDto>> ProvisionAsync(
        string repositoryPath,
        string worktreePath,
        CancellationToken cancellationToken = default);
}
