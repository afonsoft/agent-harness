using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Task = System.Threading.Tasks.Task;
using Taskboard.Agents;
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
    private readonly ISkillsSyncService? _skillsSync;
    private readonly IMcpProvisioningService? _mcpProvisioning;

    public SettingsService(
        IRepository<UserPreference> userPreferenceRepo,
        IRepository<AgentPreference> agentPreferenceRepo,
        IAgentDiscoveryService agentDiscovery,
        ISkillsSyncService? skillsSync = null,
        IMcpProvisioningService? mcpProvisioning = null)
    {
        _userPreferenceRepo = userPreferenceRepo;
        _agentPreferenceRepo = agentPreferenceRepo;
        _agentDiscovery = agentDiscovery;
        _skillsSync = skillsSync;
        _mcpProvisioning = mcpProvisioning;
    }

    public async Task<SettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var users = await _userPreferenceRepo.ListAsync(cancellationToken);
        var user = users.FirstOrDefault() ?? new UserPreference(Guid.Empty);

        var enabled = await _agentPreferenceRepo.ListAsync(cancellationToken);
        var enabledByType = enabled.ToDictionary(a => a.AgentType);

        var discovered = await _agentDiscovery.DiscoverAsync(cancellationToken);

        var agents = discovered
            .Select(a => new AgentPreferenceDto(
                a.Type,
                a.Name,
                a.ExecutablePath,
                a.Version,
                enabledByType.TryGetValue(a.Type, out var preference) && preference.Enabled,
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
        foreach (var preference in current)
        {
            await _agentPreferenceRepo.DeleteAsync(preference, cancellationToken);
        }

        var enabledTypes = request.EnabledAgents
            .Select(name => (Parsed: Enum.TryParse<AgentType>(name, out var type), Value: type))
            .Where(x => x.Parsed)
            .Select(x => x.Value)
            .ToHashSet();

        foreach (var type in enabledTypes)
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
}
