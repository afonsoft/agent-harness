using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Task = System.Threading.Tasks.Task;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Mcp;
using Taskboard.Application.Contracts.Settings;
using Taskboard.Application.Contracts.Skills;
using Taskboard.Requests;
using Taskboard.Domain.Entities;
using Taskboard.Repositories;

namespace Taskboard.Application.Settings;

public sealed class SettingsService
{
    private readonly IRepository<UserPreference> _userPreferenceRepo;
    private readonly IRepository<AgentPreference> _agentPreferenceRepo;
    private readonly IAgentDiscoveryService _agentDiscovery;
    private readonly IAgentCliStatusService _agentCliStatus;
    private readonly ISkillsSyncService? _skillsSync;
    private readonly IMcpProvisioningService? _mcpProvisioning;

    public SettingsService(
        IRepository<UserPreference> userPreferenceRepo,
        IRepository<AgentPreference> agentPreferenceRepo,
        IAgentDiscoveryService agentDiscovery,
        IAgentCliStatusService agentCliStatus,
        ISkillsSyncService? skillsSync = null,
        IMcpProvisioningService? mcpProvisioning = null)
    {
        _userPreferenceRepo = userPreferenceRepo;
        _agentPreferenceRepo = agentPreferenceRepo;
        _agentDiscovery = agentDiscovery;
        _agentCliStatus = agentCliStatus;
        _skillsSync = skillsSync;
        _mcpProvisioning = mcpProvisioning;
    }

    public async Task<SettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var users = await _userPreferenceRepo.ListAsync(cancellationToken);
        var user = users.FirstOrDefault() ?? new UserPreference(Guid.Empty);

        var preferences = await _agentPreferenceRepo.ListAsync(cancellationToken);
        var prefsByType = preferences.ToDictionary(a => a.AgentType);

        // SPEC-20260917-agent-eligibility-task-badge RF-002: only CLIs that are
        // installed AND authenticated are listed. Preference rows for agents that
        // lost authentication stay in the DB (Enabled survives a re-login).
        var eligibleTypes = await GetEligibleAgentTypesAsync(cancellationToken);

        var missing = eligibleTypes.Where(t => !prefsByType.ContainsKey(t)).ToList();
        foreach (var type in missing)
        {
            var preference = new AgentPreference(Guid.NewGuid(), type) { Enabled = true };
            await _agentPreferenceRepo.AddAsync(preference, cancellationToken);
            prefsByType[type] = preference;
        }

        if (missing.Count > 0)
        {
            await _agentPreferenceRepo.SaveChangesAsync(cancellationToken);
        }

        var discovered = await _agentDiscovery.DiscoverAsync(cancellationToken);

        var agents = discovered
            .Where(a => eligibleTypes.Contains(a.Type))
            .Select(a => new AgentPreferenceDto(
                a.Type,
                a.Name,
                a.ExecutablePath,
                a.Version,
                prefsByType.TryGetValue(a.Type, out var preference) && preference.Enabled,
                a.Description))
            .ToList()
            .AsReadOnly();

        return new SettingsDto(user.Theme, user.GitHubToken, agents);
    }

    public async Task SaveSettingsAsync(SaveSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var users = await _userPreferenceRepo.ListAsync(cancellationToken);
        var existing = users.FirstOrDefault();
        if (existing is null)
        {
            await _userPreferenceRepo.AddAsync(
                new UserPreference(Guid.Empty)
                {
                    Theme = request.Theme,
                    GitHubToken = request.GitHubToken
                },
                cancellationToken);
        }
        else
        {
            existing.Theme = request.Theme;
            existing.GitHubToken = request.GitHubToken;
            await _userPreferenceRepo.UpdateAsync(existing, cancellationToken);
        }

        var current = await _agentPreferenceRepo.ListAsync(cancellationToken);
        var previouslyEnabled = current
            .Where(p => p.Enabled)
            .Select(p => p.AgentType)
            .ToHashSet();

        // Only agents currently eligible (installed + authenticated) can have their
        // toggle changed — the UI never lists the others, and their rows keep the
        // last known Enabled value so re-authentication restores it.
        var eligibleTypes = await GetEligibleAgentTypesAsync(cancellationToken);

        var enabledTypes = request.EnabledAgents
            .Select(name => (Parsed: Enum.TryParse<AgentType>(name, out var type), Value: type))
            .Where(x => x.Parsed)
            .Select(x => x.Value)
            .Where(eligibleTypes.Contains)
            .ToHashSet();

        foreach (var preference in current.Where(p => eligibleTypes.Contains(p.AgentType)))
        {
            preference.Enabled = enabledTypes.Contains(preference.AgentType);
            await _agentPreferenceRepo.UpdateAsync(preference, cancellationToken);
        }

        var knownTypes = current.Select(p => p.AgentType).ToHashSet();
        foreach (var type in enabledTypes.Where(t => !knownTypes.Contains(t)))
        {
            await _agentPreferenceRepo.AddAsync(
                new AgentPreference(Guid.NewGuid(), type) { Enabled = true },
                cancellationToken);
        }

        await _userPreferenceRepo.SaveChangesAsync(cancellationToken);

        // SPEC-20260915-skills-repo-sync RF-007: enabling a CLI triggers a
        // skills sync for the newly enabled agents in the background.
        var newlyEnabled = enabledTypes.Where(t => !previouslyEnabled.Contains(t)).ToList();
        if (newlyEnabled.Count > 0)
        {
            _skillsSync?.RequestSync(newlyEnabled);
            // SPEC-20260917-rag-mcp-provisioning RF-004: enabling a CLI also
            // provisions the managed MCP server for the newly enabled agents.
            _mcpProvisioning?.RequestProvision(newlyEnabled);
        }
    }

    /// <summary>
    /// AgentTypes whose backing CLI is installed and authenticated right now.
    /// </summary>
    private async Task<HashSet<AgentType>> GetEligibleAgentTypesAsync(CancellationToken cancellationToken)
    {
        var statuses = await _agentCliStatus.GetStatusAsync(cancellationToken);
        return statuses
            .Where(s => s.Installed && s.AuthStatus == AgentCliAuthStatus.Authenticated)
            .Select(s => AgentCliMap.AgentTypeFor(s.Agent))
            .Where(t => t is not null)
            .Select(t => t!.Value)
            .ToHashSet();
    }
}
