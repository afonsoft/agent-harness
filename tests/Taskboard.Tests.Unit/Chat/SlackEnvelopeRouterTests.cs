using System.Text.Json;
using Shouldly;
using Taskboard.Server.Services.Slack;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>SPEC-20261025 C1 — Socket Mode envelope routing.</summary>
public sealed class SlackEnvelopeRouterTests
{
    private static JsonElement Envelope(string json) =>
        JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Dado_EnvelopeMessage_Quando_Routed_Entao_AckEInbound()
    {
        var dispatches = SlackEnvelopeRouter.Route(Envelope("""
            {"envelope_id":"e1","type":"events_api","payload":{"event":{
              "type":"message","channel":"C123","user":"U9","text":"oi harness","ts":"1700.01"}}}
            """)).ToList();

        dispatches[0].ShouldBe(new SlackAck("e1"));
        var inbound = dispatches[1].ShouldBeOfType<SlackInbound>();
        inbound.Message.ExternalChannelId.ShouldBe("C123");
        inbound.Message.ExternalThreadId.ShouldBe("1700.01");
        inbound.Message.Text.ShouldBe("oi harness");
        inbound.Message.Channel.ShouldBe("slack");
    }

    [Fact]
    public void Dado_ReplyEmThread_Quando_Routed_Entao_ThreadRaizAncora()
    {
        var inbound = SlackEnvelopeRouter.Route(Envelope("""
            {"envelope_id":"e2","type":"events_api","payload":{"event":{
              "type":"message","channel":"C123","user":"U9","text":"reply","ts":"1700.05","thread_ts":"1700.01"}}}
            """)).OfType<SlackInbound>().Single();

        inbound.Message.ExternalThreadId.ShouldBe("1700.01");
    }

    [Fact]
    public void Dado_BotMessage_Quando_Routed_Entao_Ignorada()
    {
        var dispatches = SlackEnvelopeRouter.Route(Envelope("""
            {"envelope_id":"e3","type":"events_api","payload":{"event":{
              "type":"message","bot_id":"B1","channel":"C123","text":"echo","ts":"1700.02"}}}
            """)).ToList();

        dispatches.ShouldBe([new SlackAck("e3")]);
    }

    [Fact]
    public void Dado_SubtypeEdit_Quando_Routed_Entao_Ignorada()
    {
        var dispatches = SlackEnvelopeRouter.Route(Envelope("""
            {"envelope_id":"e4","type":"events_api","payload":{"event":{
              "type":"message","subtype":"message_changed","channel":"C123","text":"x","ts":"1"}}}
            """)).ToList();

        dispatches.ShouldBe([new SlackAck("e4")]);
    }

    [Fact]
    public void Dado_ActionApprove_Quando_Routed_Entao_DecisaoComActor()
    {
        var dispatches = SlackEnvelopeRouter.Route(Envelope("""
            {"envelope_id":"e5","type":"interactive","payload":{
              "type":"block_actions",
              "user":{"id":"U9","username":"afonso"},
              "actions":[{"action_id":"harness_approval:ap-1","value":"allow"}]}}
            """)).ToList();

        var action = dispatches.OfType<SlackApprovalAction>().Single();
        action.ApprovalId.ShouldBe("ap-1");
        action.Outcome.ShouldBe("allow");
        action.Actor.ShouldBe("@afonso");
    }

    [Fact]
    public void Dado_ActionValorInvalido_Quando_Routed_Entao_Ignorada()
    {
        var dispatches = SlackEnvelopeRouter.Route(Envelope("""
            {"envelope_id":"e6","type":"interactive","payload":{
              "actions":[{"action_id":"harness_approval:ap-1","value":"maybe"}]}}
            """)).ToList();

        dispatches.OfType<SlackApprovalAction>().ShouldBeEmpty();
    }

    [Fact]
    public void Dado_Disconnect_Quando_Routed_Entao_Reconnect()
    {
        var dispatches = SlackEnvelopeRouter.Route(Envelope("""
            {"envelope_id":"e7","type":"disconnect","reason":"socket_mode_reconnect_requested"}
            """)).ToList();

        dispatches.ShouldContain(d => d is SlackReconnect);
    }

    [Fact]
    public void Dado_Hello_Quando_Routed_Entao_SoAck()
    {
        var dispatches = SlackEnvelopeRouter.Route(Envelope("""
            {"envelope_id":"e8","type":"hello"}
            """)).ToList();

        dispatches.ShouldBe([new SlackAck("e8")]);
    }
}
