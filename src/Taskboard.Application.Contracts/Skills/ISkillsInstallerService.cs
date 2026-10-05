namespace Taskboard.Application.Contracts.Skills;

/// <summary>
/// Installs the configured skills repository globally via
/// <c>npx skills add &lt;repo&gt; -g --all --copy</c> followed by the
/// repository's <c>install.sh --all</c>, and reports whether the collection
/// is installed (SPEC-20260917-skills-installer).
/// SPEC-20261010-mcp-skills-hub adds granular installs
/// (<c>npx skills add &lt;repo&gt;[@&lt;skill&gt;] -g -y --copy</c>) and an
/// optional repository override for a single run.
/// </summary>
public interface ISkillsInstallerService
{
    /// <summary>
    /// Returns the current snapshot: in-flight/last run state when available,
    /// otherwise the persisted manifest plus live prerequisites and locations.
    /// </summary>
    SkillsInstallStatus GetStatus();

    /// <summary>
    /// Starts an install in the background. Requests made while a run is in
    /// flight are coalesced — the in-flight run is not duplicated.
    /// <paramref name="repository"/> overrides the configured repository for
    /// that run only.
    /// </summary>
    void RequestInstall(string? repository = null);

    /// <summary>
    /// Runs the full install (npx + repo cache refresh + install.sh). When a
    /// run is already in flight, returns the in-flight status snapshot.
    /// <paramref name="repository"/> overrides the configured repository for
    /// that run only.
    /// </summary>
    Task<SkillsInstallStatus> InstallAsync(
        string? repository = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-scans the global skills directories and updates the manifest —
    /// never spawns install processes.
    /// </summary>
    Task<SkillsInstallStatus> VerifyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs a single skill — <c>npx skills add &lt;repository&gt;[@&lt;skill&gt;]
    /// -g -y --copy</c>. Returns the step outcome (exit code + sanitized
    /// output); the manifest records the extra install.
    /// </summary>
    Task<SkillsInstallStep> InstallSkillAsync(
        string repository, string? skill, CancellationToken cancellationToken = default);
}
