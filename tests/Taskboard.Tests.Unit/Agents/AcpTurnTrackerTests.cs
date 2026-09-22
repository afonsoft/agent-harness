using System.Text.Json;
using Shouldly;
using Taskboard.Integrations.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20260921-acp-v2-readiness RF-204: turn tracking por dialeto —
/// v1 termina no response de session/prompt; v2 termina em state_update(idle).
/// </summary>
public sealed class AcpTurnTrackerTests
{
    private static JsonElement El(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Dado_TurnoV1Aberto_Quando_ResponseChega_Entao_TerminaComStopReason()
    {
        var turn = AcpV1Dialect.Instance.CreateTurnTracker();
        turn.BeginTurn();

        var ended = turn.OnPromptResponse(El("""{"stopReason":"end_turn"}"""), out var stopReason);

        ended.ShouldBeTrue();
        stopReason.ShouldBe("end_turn");
        turn.TurnOpen.ShouldBeFalse();
    }

    [Fact]
    public void Dado_TurnoV1_Quando_SessionUpdate_Entao_NaoTermina()
    {
        var turn = AcpV1Dialect.Instance.CreateTurnTracker();
        turn.BeginTurn();

        var ended = turn.OnSessionUpdate(
            El("""{"sessionUpdate":"state_update","state":"idle","stopReason":"end_turn"}"""), out _);

        ended.ShouldBeFalse();
        turn.TurnOpen.ShouldBeTrue();
    }

    [Fact]
    public void Dado_TurnoV2Aberto_Quando_AckChega_Entao_NaoTermina()
    {
        var turn = AcpV2Dialect.Instance.CreateTurnTracker();
        turn.BeginTurn();

        var ended = turn.OnPromptResponse(El("""{"messageId":"msg-1"}"""), out var stopReason);

        ended.ShouldBeFalse();
        stopReason.ShouldBeNull();
        turn.TurnOpen.ShouldBeTrue();
    }

    [Fact]
    public void Dado_TurnoV2Aberto_Quando_StateUpdateIdle_Entao_TerminaComStopReason()
    {
        var turn = AcpV2Dialect.Instance.CreateTurnTracker();
        turn.BeginTurn();
        turn.OnPromptResponse(El("""{"messageId":"msg-1"}"""), out _);

        var ended = turn.OnSessionUpdate(
            El("""{"sessionUpdate":"state_update","state":"idle","stopReason":"end_turn"}"""),
            out var stopReason);

        ended.ShouldBeTrue();
        stopReason.ShouldBe("end_turn");
        turn.TurnOpen.ShouldBeFalse();
    }

    [Fact]
    public void Dado_TurnoV2Cancelado_Quando_IdleCancelled_Entao_Termina()
    {
        var turn = AcpV2Dialect.Instance.CreateTurnTracker();
        turn.BeginTurn();

        var ended = turn.OnSessionUpdate(
            El("""{"sessionUpdate":"state_update","state":"idle","stopReason":"cancelled"}"""),
            out var stopReason);

        ended.ShouldBeTrue();
        stopReason.ShouldBe("cancelled");
    }

    [Fact]
    public void Dado_TurnoV2Aberto_Quando_RunningOuRequiresAction_Entao_ContinuaAberto()
    {
        var turn = AcpV2Dialect.Instance.CreateTurnTracker();
        turn.BeginTurn();

        turn.OnSessionUpdate(
            El("""{"sessionUpdate":"state_update","state":"running"}"""), out _).ShouldBeFalse();
        turn.OnSessionUpdate(
            El("""{"sessionUpdate":"state_update","state":"requires_action"}"""), out _).ShouldBeFalse();
        turn.TurnOpen.ShouldBeTrue();
    }

    [Fact]
    public void Dado_TurnoV2Fechado_Quando_IdleForaDeTurno_Entao_Ignorado()
    {
        var turn = AcpV2Dialect.Instance.CreateTurnTracker();

        var ended = turn.OnSessionUpdate(
            El("""{"sessionUpdate":"state_update","state":"idle","stopReason":"end_turn"}"""), out _);

        ended.ShouldBeFalse();
    }

    [Fact]
    public void Dado_TurnoAberto_Quando_Abort_Entao_FechaERetornaTrue()
    {
        var turn = AcpV2Dialect.Instance.CreateTurnTracker();
        turn.BeginTurn();

        turn.Abort().ShouldBeTrue();
        turn.TurnOpen.ShouldBeFalse();
        turn.Abort().ShouldBeFalse();
    }
}
