using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Task = System.Threading.Tasks.Task;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Domain.Entities;
using Taskboard.Repositories;

namespace Taskboard.Application.Agents;

public sealed class AgentEligibilityService : IAgentEligibilityService
{
    private readonly IAgentCliStatusService _cliStatus;
    private readonly IRepository<AgentPreference> _agentPreferenceRepo;

    public AgentEligibilityService(
        IAgentCliStatusService cliStatus,
        IRepository<AgentPreference> agentPreferenceRepo)
    {
        _cliStatus = cliStatus;
        _agentPreferenceRepo = agentPreferenceRepo;
    }

    public async Task<IReadOnlySet<AgentType>> GetEligibleTypesAsync(CancellationToken cancellationToken = default)
    {
        var statuses = await _cliStatus.GetStatusAsync(cancellationToken);
        var authenticated = statuses
            .Where(s => s.Installed && s.AuthStatus == AgentCliAuthStatus.Authenticated)
            .Select(s => AgentCliMap.AgentTypeFor(s.Agent))
            .Where(t => t is not null)
            .Select(t => t!.Value)
            .ToHashSet();

        var preferences = await _agentPreferenceRepo.ListAsync(cancellationToken);
        if (preferences.Count == 0)
        {
            return authenticated;
        }

        var enabled = preferences.Where(p => p.Enabled).Select(p => p.AgentType).ToHashSet();
        authenticated.IntersectWith(enabled);
        return authenticated;
    }
}
