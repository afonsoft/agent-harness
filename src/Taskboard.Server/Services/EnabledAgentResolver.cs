using Microsoft.Extensions.DependencyInjection;
using Taskboard.Agents;
using Taskboard.Domain.Entities;
using Taskboard.Repositories;

namespace Taskboard.Server.Services;

/// <summary>
/// Resolves the set of agents enabled in Settings. An empty preference table
/// means "first run": every supported CLI is treated as enabled.
/// </summary>
internal static class EnabledAgentResolver
{
    public static async Task<IReadOnlyCollection<AgentType>> ResolveAsync(
        IServiceScopeFactory scopeFactory,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var preferences = await scope.ServiceProvider
            .GetRequiredService<IRepository<AgentPreference>>()
            .ListAsync(cancellationToken);

        if (preferences.Count == 0)
        {
            return Enum.GetValues<AgentType>();
        }

        return preferences
            .Where(p => p.Enabled)
            .Select(p => p.AgentType)
            .ToList();
    }
}
