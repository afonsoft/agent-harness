using System.Threading.Channels;
using Shouldly;
using Taskboard.Application.Chat;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261005-chat-background-resume RF-003: o broadcaster é a fan-out
/// seq'd dos runs — assinante recebe deltas em ordem, o checkpoint acumula
/// os parciais e Complete fecha o canal sem perder eventos já publicados.
/// </summary>
public sealed class ChatRunBroadcasterTests
{
    [Fact]
    public async Task Dado_RunAtiva_Quando_Publish_Entao_SeqIncrementaEAssinanteRecebeEmOrdem()
    {
        var broadcaster = new ChatRunBroadcaster();
        var reader = broadcaster.Subscribe("run-1");

        broadcaster.Publish("run-1", new ChatDeltaEvent("a"));
        broadcaster.Publish("run-1", new ChatDeltaEvent("b"));
        broadcaster.Complete("run-1");

        var received = new List<ChatRunEventEnvelope>();
        await foreach (var envelope in reader.ReadAllAsync())
        {
            received.Add(envelope);
        }

        received.Select(e => e.Seq).ShouldBe([1, 2]);
        received.Select(e => ((ChatDeltaEvent)e.Event).Content).ShouldBe(["a", "b"]);
    }

    [Fact]
    public void Dado_DeltasPublicados_Quando_GetLive_Entao_ParcialAcumulado()
    {
        var broadcaster = new ChatRunBroadcaster();
        broadcaster.Publish("run-2", new ChatDeltaEvent("olá"));
        broadcaster.Publish("run-2", new ChatReasoningEvent("pensando"));
        broadcaster.Publish("run-2", new ChatDeltaEvent(" mundo"));

        var live = broadcaster.GetLive("run-2");

        live.ShouldNotBeNull();
        live.Partial.ShouldBe("olá mundo");
        live.PartialReasoning.ShouldBe("pensando");
        live.LastSeq.ShouldBe(3);
    }

    [Fact]
    public void Dado_ChatPersisted_Quando_GetLive_Entao_ParciaisLimpam()
    {
        // O turno que já virou linha durável sai do checkpoint — evita
        // duplicar conteúdo entre chat.sync.messages e o partial.
        var broadcaster = new ChatRunBroadcaster();
        broadcaster.Publish("run-3", new ChatDeltaEvent("primeira resposta"));
        broadcaster.Publish("run-3", new ChatPersistedEvent());
        broadcaster.Publish("run-3", new ChatDeltaEvent("próximo turno"));

        var live = broadcaster.GetLive("run-3");

        live.ShouldNotBeNull();
        live.Partial.ShouldBe("próximo turno");
    }

    [Fact]
    public async Task Dado_SubscribeAposComplete_Quando_Ler_Entao_TerminaSemTravar()
    {
        // Um attach tardio a um run já encerrado recebe um stream vazio e
        // fechado — o endpoint cobre o gap com o snapshot (chat.sync) e o
        // chat.done terminal; nunca fica pendurado esperando evento.
        var broadcaster = new ChatRunBroadcaster();
        broadcaster.Publish("run-4", new ChatDeltaEvent("x"));
        broadcaster.Complete("run-4");

        var reader = broadcaster.Subscribe("run-4");
        var received = await ReadAllAsync(reader);

        received.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_DoisAssinantes_Quando_Publish_Entao_AmbosRecebem()
    {
        var broadcaster = new ChatRunBroadcaster();
        var first = broadcaster.Subscribe("run-5");
        var second = broadcaster.Subscribe("run-5");

        broadcaster.Publish("run-5", new ChatDeltaEvent("fanout"));
        broadcaster.Complete("run-5");

        (await ReadAllAsync(first)).Count.ShouldBe(1);
        (await ReadAllAsync(second)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_CompleteSemAssinante_Quando_GetLive_Entao_Null()
    {
        var broadcaster = new ChatRunBroadcaster();
        broadcaster.Publish("run-6", new ChatDeltaEvent("x"));
        broadcaster.Complete("run-6");

        var live = broadcaster.GetLive("run-6");

        // O live state sai de cena; a linha durável do run carrega o final.
        live.ShouldBeNull();

        var reader = broadcaster.Subscribe("run-6");
        (await ReadAllAsync(reader)).ShouldBeEmpty();
    }

    private static async Task<List<ChatRunEventEnvelope>> ReadAllAsync(
        ChannelReader<ChatRunEventEnvelope> reader)
    {
        var items = new List<ChatRunEventEnvelope>();
        await foreach (var envelope in reader.ReadAllAsync())
        {
            items.Add(envelope);
        }

        return items;
    }
}
