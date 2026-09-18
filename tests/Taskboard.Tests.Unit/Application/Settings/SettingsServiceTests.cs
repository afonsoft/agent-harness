using System;
using System.Collections.Generic;
using System.Threading;
using Task = System.Threading.Tasks.Task;
using NSubstitute;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
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
    private static IAgentCliStatusService CliStatus(params AgentCliStatus[] statuses)
    {
        var service = Substitute.For<IAgentCliStatusService>();
        service.GetStatusAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AgentCliStatus>>(statuses.ToList()));
        return service;
    }

    private static AgentCliStatus Authenticated(AgentCliKind kind) =>
        new(kind, kind.ToString(), "bin", true, "1.0", AgentCliAuthStatus.Authenticated, "~", "login", "install", "npm", true);

    private static AgentCliStatus NotAuthenticated(AgentCliKind kind) =>
        new(kind, kind.ToString(), "bin", true, "1.0", AgentCliAuthStatus.NotAuthenticated, "~", "login", "install", "npm", true);

    [Fact]
    public async Task Dado_PreferenciasExistentes_Quando_Obter_Entao_RetornaTemaEAgentesAutenticados()
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
                new AgentInfo("claude", "/usr/bin/claude", AgentType.Claude, AgentStatus.Available, "1.0", null),
                new AgentInfo("codex", "/usr/bin/codex", AgentType.Codex, AgentStatus.Available, "1.0", null)
            }));

        var service = new SettingsService(
            userRepo, agentRepo, discovery,
            CliStatus(Authenticated(AgentCliKind.Claude), NotAuthenticated(AgentCliKind.Codex)));

        var settings = await service.GetSettingsAsync();

        settings.Theme.ShouldBe("light");
        settings.GitHubToken.ShouldBe("ghp_***");
        settings.Agents.Count.ShouldBe(1);
        settings.Agents[0].Type.ShouldBe(AgentType.Claude);
        settings.Agents[0].Enabled.ShouldBe(false);
    }

    [Fact]
    public async Task Dado_CliAutenticadoSemPreferencia_Quando_Obter_Entao_CriaLinhaHabilitada()
    {
        var userRepo = Substitute.For<IRepository<UserPreference>>();
        userRepo.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<UserPreference>>(new List<UserPreference>()));

        var added = new List<AgentPreference>();
        var agentRepo = Substitute.For<IRepository<AgentPreference>>();
        agentRepo.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AgentPreference>>(new List<AgentPreference>()));
        agentRepo.AddAsync(Arg.Do<AgentPreference>(added.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var discovery = Substitute.For<IAgentDiscoveryService>();
        discovery.DiscoverAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AgentInfo>>(new List<AgentInfo>
            {
                new AgentInfo("agy", "/usr/bin/agy", AgentType.Antigravity, AgentStatus.Available, "1.2", null)
            }));

        var service = new SettingsService(
            userRepo, agentRepo, discovery,
            CliStatus(Authenticated(AgentCliKind.Antigravity)));

        var settings = await service.GetSettingsAsync();

        added.Count.ShouldBe(1);
        added[0].AgentType.ShouldBe(AgentType.Antigravity);
        added[0].Enabled.ShouldBeTrue();
        settings.Agents.Single().Enabled.ShouldBeTrue();
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
        var service = new SettingsService(
            userRepo, agentRepo, discovery,
            CliStatus(Authenticated(AgentCliKind.Devin), Authenticated(AgentCliKind.Claude)),
            sync);

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
        var service = new SettingsService(
            userRepo, agentRepo, discovery,
            CliStatus(Authenticated(AgentCliKind.Claude)),
            sync);

        await service.SaveSettingsAsync(
            new SaveSettingsRequest("dark", null, ["Claude"]));

        sync.DidNotReceive().RequestSync(Arg.Any<IReadOnlyCollection<AgentType>>());
    }

    [Fact]
    public async Task Dado_AgenteNaoAutenticado_Quando_Salvar_Entao_PreservaEnabledDaLinha()
    {
        // SPEC RF-002: hidden (unauthenticated) agents keep their Enabled value.
        var hiddenPref = new AgentPreference(Guid.NewGuid(), AgentType.Codex) { Enabled = true };
        var visiblePref = new AgentPreference(Guid.NewGuid(), AgentType.Claude) { Enabled = true };

        var userRepo = Substitute.For<IRepository<UserPreference>>();
        userRepo.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<UserPreference>>(new List<UserPreference>()));

        var agentRepo = Substitute.For<IRepository<AgentPreference>>();
        agentRepo.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AgentPreference>>(new List<AgentPreference>
            {
                hiddenPref, visiblePref
            }));

        var service = new SettingsService(
            userRepo, agentRepo,
            Substitute.For<IAgentDiscoveryService>(),
            // Codex lost auth — it is not listed in the UI and not in the request.
            CliStatus(Authenticated(AgentCliKind.Claude), NotAuthenticated(AgentCliKind.Codex)));

        await service.SaveSettingsAsync(new SaveSettingsRequest("dark", null, []));

        hiddenPref.Enabled.ShouldBeTrue("linha de agente oculto preserva Enabled");
        visiblePref.Enabled.ShouldBeFalse("agente visível removido do request fica desabilitado");
        await agentRepo.DidNotReceive().DeleteAsync(Arg.Any<AgentPreference>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_RequestComAgenteNaoElegivel_Quando_Salvar_Entao_IgnoraTipo()
    {
        // SPEC RF-003: the server never enables an unauthenticated agent even if requested.
        var userRepo = Substitute.For<IRepository<UserPreference>>();
        userRepo.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<UserPreference>>(new List<UserPreference>()));

        var agentRepo = Substitute.For<IRepository<AgentPreference>>();
        agentRepo.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AgentPreference>>(new List<AgentPreference>()));

        var sync = Substitute.For<ISkillsSyncService>();
        var service = new SettingsService(
            userRepo, agentRepo,
            Substitute.For<IAgentDiscoveryService>(),
            CliStatus(NotAuthenticated(AgentCliKind.Codex)),
            sync);

        await service.SaveSettingsAsync(new SaveSettingsRequest("dark", null, ["Codex", "OpenHands"]));

        await agentRepo.DidNotReceive().AddAsync(Arg.Any<AgentPreference>(), Arg.Any<CancellationToken>());
        sync.DidNotReceive().RequestSync(Arg.Any<IReadOnlyCollection<AgentType>>());
    }
}
