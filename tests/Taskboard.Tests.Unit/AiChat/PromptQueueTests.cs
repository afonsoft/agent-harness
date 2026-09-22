using Shouldly;
using Taskboard.Server.Services;
using Xunit;

namespace Taskboard.Tests.Unit.AiChat;

/// <summary>
/// SPEC-20260921-ai-code-chat-ux RF-002/NFR-002: fila FIFO por thread com
/// dispatch serializado — só um session/prompt em voo por thread.
/// </summary>
public class PromptQueueTests
{
    [Fact]
    public void Dado_FilaVaziaSemTurno_Quando_Enqueue_Entao_DispatchImediato()
    {
        var q = new PromptQueue();

        q.Enqueue("e1");

        q.TryDispatch(out var id).ShouldBeTrue();
        id.ShouldBe("e1");
        q.TurnActive.ShouldBeTrue();
    }

    [Fact]
    public void Dado_TurnoAtivo_Quando_Enqueue_Entao_NaoDespacha()
    {
        var q = new PromptQueue();
        q.MarkTurnActive();

        q.Enqueue("e1");

        q.TryDispatch(out _).ShouldBeFalse();
        q.Pending.ShouldBe(["e1"]);
    }

    [Fact]
    public void Dado_DoisItens_Quando_TurnoCompleta_Entao_DespachaFifo()
    {
        var q = new PromptQueue();
        q.MarkTurnActive();
        q.Enqueue("e1");
        q.Enqueue("e2");

        q.TurnCompleted();
        q.TryDispatch(out var first).ShouldBeTrue();
        first.ShouldBe("e1");

        q.TurnCompleted();
        q.TryDispatch(out var second).ShouldBeTrue();
        second.ShouldBe("e2");
    }

    [Fact]
    public void Dado_DespachoFalha_Quando_DispatchFailed_Entao_ReenfileiraNaFrente()
    {
        var q = new PromptQueue();
        q.Enqueue("e1");
        q.Enqueue("e2");
        q.TryDispatch(out var id).ShouldBeTrue();

        q.DispatchFailed(id!);

        q.TurnActive.ShouldBeFalse();
        q.Pending.ShouldBe(["e1", "e2"]);
    }

    [Fact]
    public void Dado_ItemEnfileirado_Quando_Remove_Entao_SaiDaFila()
    {
        var q = new PromptQueue();
        q.MarkTurnActive();
        q.Enqueue("e1");
        q.Enqueue("e2");

        q.Remove("e1").ShouldBeTrue();

        q.Pending.ShouldBe(["e2"]);
        q.Remove("e1").ShouldBeFalse();
    }

    [Fact]
    public void Dado_SessaoMorta_Quando_Reset_Entao_TurnoLiberadoEFilaPreservada()
    {
        var q = new PromptQueue();
        q.Enqueue("e1");
        q.TryDispatch(out _).ShouldBeTrue();
        q.Enqueue("e2");

        q.Reset();

        q.TurnActive.ShouldBeFalse();
        q.Pending.ShouldBe(["e2"]);
    }

    [Fact]
    public void Dado_DispatchConcorrente_Quando_TryDispatch_Entao_ApenasUmVence()
    {
        // NFR-002: TryDispatch é atômico — duas chamadas não despacham o mesmo item.
        var q = new PromptQueue();
        q.Enqueue("e1");

        q.TryDispatch(out var first).ShouldBeTrue();
        q.TryDispatch(out var second).ShouldBeFalse();

        first.ShouldBe("e1");
        second.ShouldBeNull();
    }
}
