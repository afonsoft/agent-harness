using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.Delegation;

/// <summary>
/// SPEC-20261020-v2 RF-008: resolves requested launch preferences
/// (<c>model</c>/<c>effort</c>) against a target CLI. Unknown capability is an
/// explicit error — never silently dropped.
/// </summary>
public interface IAgentLaunchResolver
{
    /// <summary>
    /// Maps the request to effective values + extra CLI args. Throws
    /// <c>DomainException launch_preference_unsupported</c> when the CLI cannot
    /// honor the requested preference.
    /// </summary>
    Task<LaunchResolution> ResolveLaunchAsync(
        string cli, string? model, string? effort, CancellationToken ct = default);
}
