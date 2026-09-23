namespace Taskboard.Application.Contracts.Harness;

/// <summary>
/// Result of <see cref="IRepositoryProvisioningService.EnsureCloneAsync"/> —
/// the absolute clone path plus whether a fresh <c>git clone</c> ran.
/// </summary>
public sealed record RepositoryCloneResult(string Path, bool Cloned, long ElapsedMs);

/// <summary>
/// Guarantees a real clone of <c>owner/name</c> exists under the workspace
/// root (<c>~/repos/&lt;name&gt;</c>) before a pipeline run creates its
/// worktree — never falls back to the workspace root itself
/// (SPEC-20260923-cockpit-run-hardening RF-001).
/// </summary>
public interface IRepositoryProvisioningService
{
    /// <summary>
    /// Expected clone path <c>&lt;root&gt;/&lt;name&gt;</c> without touching the
    /// filesystem — used to fill <c>RepositoryPath</c> before the clone runs.
    /// Throws <see cref="DomainException"/> when the name is invalid.
    /// </summary>
    string ResolveClonePath(string repositoryFullName);

    /// <summary>
    /// Returns the clone path, reusing an existing matching clone or running
    /// <c>git clone</c> when missing. Throws
    /// <see cref="TaskboardDomainErrorCodes.RepositoryProvisioningFailed"/>
    /// when the path holds a foreign repo/non-repo or the clone fails.
    /// </summary>
    Task<RepositoryCloneResult> EnsureCloneAsync(
        string repositoryFullName, CancellationToken cancellationToken = default);
}
