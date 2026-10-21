using Taskboard;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Dtos;

namespace Taskboard.Application.Delegation;

/// <summary>
/// SPEC-20261020-v2 RF-008: capability table for <c>model</c>/<c>effort</c>.
/// Builtin CLIs accept both (effort → <see cref="AgentModelTier"/>); custom
/// defs accept model only when the def declares a <c>ModelFlag</c>; effort is
/// unsupported there. Unknown target + prefs →
/// <c>launch_preference_unsupported</c>.
/// </summary>
public sealed class AgentLaunchResolver(IAgentCliDefinitionRepository defs) : IAgentLaunchResolver
{
    public async Task<LaunchResolution> ResolveLaunchAsync(
        string cli, string? model, string? effort, CancellationToken ct = default)
    {
        if (model is null && effort is null)
        {
            return new LaunchResolution(null, null, []);
        }

        if (string.IsNullOrWhiteSpace(cli))
        {
            throw Unsupported(cli, "no cli given");
        }

        var effectiveEffort = effort is null
            ? null
            : AgentLaunchEffort.ToTier(effort) is { } tier
                ? tier.ToString().ToLowerInvariant()
                : throw Unsupported(cli, $"unknown effort '{effort}' (use low|normal|high)");

        // Builtin CLI — model + effort both supported.
        if (Enum.TryParse(cli, ignoreCase: true, out AgentType _))
        {
            return new LaunchResolution(model, effectiveEffort, []);
        }

        // Custom def — model only when the def declares a model flag; effort never.
        var def = (await defs.ListAsync(ct).ConfigureAwait(false))
            .FirstOrDefault(d =>
                string.Equals(d.Id, cli, StringComparison.OrdinalIgnoreCase)
                || string.Equals(d.DisplayName, cli, StringComparison.OrdinalIgnoreCase)
                || string.Equals(d.Executable, cli, StringComparison.OrdinalIgnoreCase));
        if (def is null)
        {
            throw Unsupported(cli, "unknown cli");
        }

        if (effort is not null)
        {
            throw Unsupported(cli, "custom defs do not support effort");
        }

        if (model is not null && string.IsNullOrWhiteSpace(def.ModelFlag))
        {
            throw Unsupported(cli, "def declares no model flag");
        }

        return new LaunchResolution(model, null, []);
    }

    private static DomainException Unsupported(string cli, string detail) =>
        new(TaskboardDomainErrorCodes.InvalidValue,
            $"launch_preference_unsupported: {cli} — {detail}");
}
