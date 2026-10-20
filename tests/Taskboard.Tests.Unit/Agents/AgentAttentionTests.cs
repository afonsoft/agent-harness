using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Agents;
using Taskboard.Application.Contracts.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20261020-agent-attention-model: máquina de transição com histerese —
/// sinais fortes cometem imediato; fracos exigem repetição; silêncio nunca vira
/// exited; waiting/blocked só saem por sinal forte.
/// </summary>
public class AgentAttentionEvaluatorTests
{
    private static AgentSessionStateDto State(
        AgentAttentionState state = AgentAttentionState.Working, int weak = 0) =>
        new("task:t1", "codex", state, null, DateTime.UtcNow, DateTime.UtcNow,
            AttentionSignalKind.Lifecycle, null, 1, weak);

    private static AttentionSignal Sig(
        AttentionSignalKind kind, AgentAttentionState next) =>
        new("task:t1", "codex", kind, next);

    [Fact]
    public void Dado_SinalForte_Quando_Avalia_Entao_ComiteImediato()
    {
        var next = AgentAttentionEvaluator.Evaluate(
            State(), Sig(AttentionSignalKind.Lifecycle, AgentAttentionState.Done),
            out var weak, out var reset);

        next.ShouldBe(AgentAttentionState.Done);
        weak.ShouldBe(0);
    }

    [Fact]
    public void Dado_UmSinalFraco_Quando_Avalia_Entao_NaoComete()
    {
        var next = AgentAttentionEvaluator.Evaluate(
            State(), Sig(AttentionSignalKind.OutputPattern, AgentAttentionState.WaitingYou),
            out var weak, out _);

        next.ShouldBeNull();
        weak.ShouldBe(1);
    }

    [Fact]
    public void Dado_DoisSinaisFracos_Quando_Avalia_Entao_Comete()
    {
        var next = AgentAttentionEvaluator.Evaluate(
            State(weak: 1), Sig(AttentionSignalKind.OutputPattern, AgentAttentionState.WaitingYou),
            out _, out _);

        next.ShouldBe(AgentAttentionState.WaitingYou);
    }

    [Fact]
    public void Dado_Aguardando_Quando_SinalFracoAlive_Entao_MantemAguardando()
    {
        // weak "alive" nunca tira de waiting_you — precisa de sinal forte.
        var next = AgentAttentionEvaluator.Evaluate(
            State(AgentAttentionState.WaitingYou),
            Sig(AttentionSignalKind.Osc, AgentAttentionState.Working),
            out _, out _);

        next.ShouldBeNull();
    }

    [Fact]
    public void Dado_Forte_Quando_Reseta_Entao_ZeraContadorFraco()
    {
        AgentAttentionEvaluator.Evaluate(
            State(weak: 1), Sig(AttentionSignalKind.Mailbox, AgentAttentionState.WaitingYou),
            out var weak, out var reset);

        weak.ShouldBe(0);
        reset.ShouldBeTrue();
    }
}

/// <summary>Serviço: pipeline serialize→store→broadcast por session key.</summary>
public class AgentAttentionServiceTests
{
    private static IServiceProvider ProviderWith(IAgentSessionStateRepository repo) =>
        new ServiceCollection().AddSingleton(repo).BuildServiceProvider();

    [Fact]
    public async Task Dado_PrimeiroSinal_Quando_Publica_Entao_CriaETransmite()
    {
        var repo = Substitute.For<IAgentSessionStateRepository>();
        repo.GetAsync("task:t1", Arg.Any<CancellationToken>()).Returns((AgentSessionStateDto?)null);
        var dto = new AgentSessionStateDto(
            "task:t1", "codex", AgentAttentionState.Working, null,
            DateTime.UtcNow, DateTime.UtcNow, AttentionSignalKind.Lifecycle, null, 1, 0);
        repo.ApplyAsync(
                Arg.Any<AttentionSignal>(), Arg.Any<AgentAttentionState>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((dto, true));
        var broadcaster = Substitute.For<IAgentAttentionBroadcaster>();
        var svc = new AgentAttentionService(ProviderWith(repo), broadcaster);

        var result = await svc.PublishAsync(
            new AttentionSignal("task:t1", "codex",
                AttentionSignalKind.Lifecycle, AgentAttentionState.Working));

        result.State.ShouldBe(AgentAttentionState.Working);
        await repo.Received(1).EnsureAsync("task:t1", "codex",
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await broadcaster.Received(1).BroadcastAsync(dto, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_SinalSemMudanca_Quando_Publica_Entao_NaoTransmite()
    {
        var repo = Substitute.For<IAgentSessionStateRepository>();
        var dto = new AgentSessionStateDto(
            "task:t1", "codex", AgentAttentionState.Working, null,
            DateTime.UtcNow, DateTime.UtcNow, AttentionSignalKind.Lifecycle, null, 1, 0);
        repo.GetAsync("task:t1", Arg.Any<CancellationToken>()).Returns(dto);
        repo.ApplyAsync(
                Arg.Any<AttentionSignal>(), Arg.Any<AgentAttentionState>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((dto, false));
        var broadcaster = Substitute.For<IAgentAttentionBroadcaster>();
        var svc = new AgentAttentionService(ProviderWith(repo), broadcaster);

        await svc.PublishAsync(
            new AttentionSignal("task:t1", "codex",
                AttentionSignalKind.Lifecycle, AgentAttentionState.Working));

        await broadcaster.DidNotReceive().BroadcastAsync(
            Arg.Any<AgentSessionStateDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_SessoesSilenciosas_Quando_Sweep_Entao_MarcaUnverifiable()
    {
        var repo = Substitute.For<IAgentSessionStateRepository>();
        var silent = new AgentSessionStateDto(
            "task:t9", "claude", AgentAttentionState.Working, null,
            DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(-10),
            AttentionSignalKind.Lifecycle, null, 1, 0);
        repo.ListSilentAsync(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([silent]);
        repo.GetAsync("task:t9", Arg.Any<CancellationToken>()).Returns(silent);
        var unverifiable = silent with
        {
            State = AgentAttentionState.Unverifiable,
        };
        repo.ApplyAsync(
                Arg.Any<AttentionSignal>(),
                Arg.Is<AgentAttentionState>(s => s == AgentAttentionState.Unverifiable),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((unverifiable, true));
        var svc = new AgentAttentionService(
            ProviderWith(repo), Substitute.For<IAgentAttentionBroadcaster>());

        var swept = await svc.SweepUnverifiableAsync(TimeSpan.FromMinutes(5));

        swept.ShouldBe(1);
        await repo.Received(1).ApplyAsync(
            Arg.Is<AttentionSignal>(s => s.SuggestedState == AgentAttentionState.Unverifiable),
            Arg.Is<AgentAttentionState>(s => s == AgentAttentionState.Unverifiable),
            Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
}
