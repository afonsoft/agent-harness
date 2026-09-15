using System;
using System.Collections.Generic;
using System.Threading;
using Task = System.Threading.Tasks.Task;
using NSubstitute;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Settings;
using Taskboard.Application.Contracts.Skills;
using Taskboard.Application.Settings;
using Taskboard.Domain.Entities;
using Taskboard.Repositories;
using Taskboard.Requests;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Application.Settings;

public class SettingsServiceTests
{
    [Fact]
    public async Task Dado_PreferenciasExistentes_Quando_Obter_Entao_RetornaTemaEAgentes()
    {
        var userRepo = Substitute.For<IRepository<UserPreference>>();
        userRepo.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<UserPreference>>(new List<UserPreference>
            {
                new UserPreference(Guid.Empty) { Theme = "light", GitHubToken = "ghp_***" }
            }));

        var agentRepo = Substitute.For<IRepository<AgentPreference>>();
        agentRepo.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AgentPreference>>(new List<AgentPreference>
            {
                new AgentPreference(Guid.NewGuid(), AgentType.Claude) { Enabled = false }
            }));

        var discovery = Substitute.For<IAgentDiscoveryService>();
        discovery.DiscoverAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AgentInfo>>(new List<AgentInfo>
            {
                new AgentInfo("claude", "/usr/bin/claude", AgentType.Claude, AgentStatus.Available, "1.0", null)
            }));

        var service = new SettingsService(userRepo, agentRepo, discovery);

        var settings = await service.GetSettingsAsync();

        settings.Theme.ShouldBe("light");
        settings.GitHubToken.ShouldBe("ghp_***");
        settings.Agents.Count.ShouldBe(1);
        settings.Agents[0].Type.ShouldBe(AgentType.Claude);
        settings.Agents[0].Enabled.ShouldBe(false);
    }

    [Fact]
    public async Task Dado_AgenteRecemHabilitado_Quando_Salvar_Entao_SolicitaSyncDeSkills()
    {
        // Covers SPEC-20260915-skills-repo-sync RF-007: enabling a CLI triggers a sync
        var userRepo = Substitute.For<IRepository<UserPreference>>();
        userRepo.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<UserPreference>>(new List<UserPreference>()));

        var agentRepo = Substitute.For<IRepository<AgentPreference>>();
        agentRepo.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AgentPreference>>(new List<AgentPreference>
            {
                new AgentPreference(Guid.NewGuid(), AgentType.Devin) { Enabled = true }
            }));

        var discovery = Substitute.For<IAgentDiscoveryService>();
        var sync = Substitute.For<ISkillsSyncService>();
        var service = new SettingsService(userRepo, agentRepo, discovery, sync);

        await service.SaveSettingsAsync(
            new SaveSettingsRequest("dark", null, ["Devin", "Claude"]));

        sync.Received(1).RequestSync(
            Arg.Is<IReadOnlyCollection<AgentType>>(agents =>
                agents.Count == 1 && agents.Contains(AgentType.Claude)));
    }

    [Fact]
    public async Task Dado_SemNovoAgenteHabilitado_Quando_Salvar_Entao_NaoSolicitaSync()
    {
        // Covers RF-007: saving without newly enabled agents does not re-sync
        var userRepo = Substitute.For<IRepository<UserPreference>>();
        userRepo.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<UserPreference>>(new List<UserPreference>()));

        var agentRepo = Substitute.For<IRepository<AgentPreference>>();
        agentRepo.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AgentPreference>>(new List<AgentPreference>
            {
                new AgentPreference(Guid.NewGuid(), AgentType.Claude) { Enabled = true }
            }));

        var discovery = Substitute.For<IAgentDiscoveryService>();
        var sync = Substitute.For<ISkillsSyncService>();
        var service = new SettingsService(userRepo, agentRepo, discovery, sync);

        await service.SaveSettingsAsync(
            new SaveSettingsRequest("dark", null, ["Claude"]));

        sync.DidNotReceive().RequestSync(Arg.Any<IReadOnlyCollection<AgentType>>());
    }
}
