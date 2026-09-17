namespace Taskboard.Application.Contracts.Skills;

/// <summary>
/// Installs the configured skills repository globally via
/// <c>npx skills add &lt;repo&gt; -g --all --copy</c> followed by the
/// repository's <c>install.sh --all</c>, and reports whether the collection
/// is installed (SPEC-20260917-skills-installer).
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
    /// </summary>
    void RequestInstall();

    /// <summary>
    /// Runs the full install (npx + repo cache refresh + install.sh). When a
    /// run is already in flight, returns the in-flight status snapshot.
    /// </summary>
    Task<SkillsInstallStatus> InstallAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-scans the global skills directories and updates the manifest —
    /// never spawns install processes.
    /// </summary>
    Task<SkillsInstallStatus> VerifyAsync(CancellationToken cancellationToken = default);
}
