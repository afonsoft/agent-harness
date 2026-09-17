using System;
using System.Collections.Generic;
using System.Threading;
using Task = System.Threading.Tasks.Task;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Domain.Entities;
using Taskboard.Repositories;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

public class AgentEligibilityServiceTests
{
    [Fact]
    public async Task Dado_SemPreferencias_Quando_Consultar_Entao_TodosAutenticadosElegiveis()
    {
        var service = CriarServico(
            statuses:
            [
                Autenticado(AgentCliKind.Claude),
                NaoAutenticado(AgentCliKind.Codex)
            ],
            preferences: []);

        var eligible = await service.GetEligibleTypesAsync();

        eligible.ShouldBe([AgentType.Claude]);
    }

    [Fact]
    public async Task Dado_AgenteDesabilitado_Quando_Consultar_Entao_NaoElegivel()
    {
        var service = CriarServico(
            statuses: [Autenticado(AgentCliKind.Claude), Autenticado(AgentCliKind.Codex)],
            preferences:
            [
                new AgentPreference(Guid.NewGuid(), AgentType.Claude) { Enabled = true },
                new AgentPreference(Guid.NewGuid(), AgentType.Codex) { Enabled = false }
            ]);

        var eligible = await service.GetEligibleTypesAsync();

        eligible.ShouldBe([AgentType.Claude]);
    }

    [Fact]
    public async Task Dado_CliNaoInstalado_Quando_Consultar_Entao_NaoElegivel()
    {
        var service = CriarServico(
            statuses:
            [
                new AgentCliStatus(AgentCliKind.Claude, "Claude", "claude", false, null,
                    AgentCliAuthStatus.Unknown, "~", "claude", "install")
            ],
            preferences: []);

        var eligible = await service.GetEligibleTypesAsync();

        eligible.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_OpenHandsHabilitado_Quando_Consultar_Entao_NuncaElegivel()
    {
        // OpenHands has no CLI mapping — can never be eligible.
        var service = CriarServico(
            statuses: [],
            preferences:
            [
                new AgentPreference(Guid.NewGuid(), AgentType.OpenHands) { Enabled = true }
            ]);

        var eligible = await service.GetEligibleTypesAsync();

        eligible.ShouldNotContain(AgentType.OpenHands);
    }

    private static AgentEligibilityService CriarServico(
        AgentCliStatus[] statuses,
        AgentPreference[] preferences)
    {
        var cliStatus = Substitute.For<IAgentCliStatusService>();
        cliStatus.GetStatusAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AgentCliStatus>>(statuses.ToList()));

        var repo = Substitute.For<IRepository<AgentPreference>>();
        repo.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AgentPreference>>(preferences.ToList()));

        return new AgentEligibilityService(cliStatus, repo);
    }

    private static AgentCliStatus Autenticado(AgentCliKind kind) =>
        new(kind, kind.ToString(), "bin", true, "1.0", AgentCliAuthStatus.Authenticated, "~", "login", "install");

    private static AgentCliStatus NaoAutenticado(AgentCliKind kind) =>
        new(kind, kind.ToString(), "bin", true, "1.0", AgentCliAuthStatus.NotAuthenticated, "~", "login", "install");
}
