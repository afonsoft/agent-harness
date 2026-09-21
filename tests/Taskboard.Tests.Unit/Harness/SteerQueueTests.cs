using Shouldly;
using Taskboard.Integrations.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

/// <summary>
/// SPEC-20260919-ade-cockpit-hitl RF-003 — fila de steer por run:
/// enfileira, drena na próxima etapa e esvazia.
/// </summary>
public class SteerQueueTests
{
    private readonly SteerQueue _queue = new();

    [Fact]
    public void Dado_InstrucoesEnfileiradas_Quando_Drain_Entao_RetornaNaOrdemEEsvazia()
    {
        _queue.Enqueue("run-1", "corrija o formato");
        _queue.Enqueue("run-1", "adicione testes");
        _queue.Enqueue("run-2", "outro run");

        var drained = _queue.Drain("run-1");

        drained.ShouldBe(["corrija o formato", "adicione testes"]);
        _queue.Drain("run-1").ShouldBeEmpty();
        _queue.Drain("run-2").ShouldBe(["outro run"]);
    }

    [Fact]
    public void Dado_RunSemInstrucoes_Quando_Drain_Entao_Vazio()
    {
        _queue.Drain("sem-nada").ShouldBeEmpty();
    }

    [Fact]
    public void Dado_InstrucaoVazia_Quando_Enqueue_Entao_Ignorada()
    {
        _queue.Enqueue("run-1", "   ");
        _queue.Enqueue("run-1", "");

        _queue.Drain("run-1").ShouldBeEmpty();
    }
}
