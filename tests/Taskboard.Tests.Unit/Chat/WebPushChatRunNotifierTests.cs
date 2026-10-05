using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Application.Configuration;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Domain.Entities;
using Taskboard.Domain.Entities.Chat;
using Taskboard.Repositories;
using Taskboard.Server.Services;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261005-chat-background-resume RF-009: o Web Push notifier respeita o
/// toggle global, envia o payload com deep-link e poda subscriptions mortas
/// (404/410 — RFC 8030 §7.3).
/// </summary>
public class WebPushChatRunNotifierTests
{
    private readonly IRepository<ChatPushSubscription> _subscriptions = Substitute.For<IRepository<ChatPushSubscription>>();
    private readonly IRepository<ChatConversation> _conversations = Substitute.For<IRepository<ChatConversation>>();
    private readonly IWebPushSender _sender = Substitute.For<IWebPushSender>();

    private WebPushChatRunNotifier NewNotifier(bool pushEnabled)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Taskboard:Chat:Notify:Done:Push"] = pushEnabled ? "true" : "false",
            })
            .Build();
        var overrides = Substitute.For<IRepository<ConfigurationOverride>>();
        overrides.Query.Returns(Enumerable.Empty<ConfigurationOverride>().AsQueryable());
        var runtime = new RuntimeConfigurationService(configuration, overrides);
        return new WebPushChatRunNotifier(
            _subscriptions, _conversations, _sender, runtime,
            NullLogger<WebPushChatRunNotifier>.Instance);
    }

    private static ChatRun NewRun(ChatConversationId conversationId)
    {
        var run = ChatRun.Create(ChatRunId.NewGuid(), conversationId, ChatMessageId.NewGuid());
        run.Start();
        run.Complete(tokensIn: 10, tokensOut: 20);
        return run;
    }

    private static ChatPushSubscription NewSub(string endpoint) =>
        ChatPushSubscription.Create(
            ChatPushSubscriptionId.NewGuid(), endpoint, "p256dh-key", "auth-secret");

    [Fact]
    public async Task Dado_ToggleDesligado_Quando_Notifica_Entao_NaoEnviaPush()
    {
        _subscriptions.ListAsync(Arg.Any<CancellationToken>())
            .Returns([NewSub("https://push.example/sub/1")]);

        await NewNotifier(pushEnabled: false).RunCompletedAsync(
            NewRun(ChatConversationId.NewGuid()), CancellationToken.None);

        await _sender.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_ToggleLigado_Quando_Notifica_Entao_EnviaPayloadComDeepLinkParaCadaSub()
    {
        var conversationId = ChatConversationId.NewGuid();
        var conversation = ChatConversation.Create(
            conversationId, Guid.NewGuid(), "fake", "model", title: "Revisao do PR");
        _conversations.GetAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(conversation);
        var subs = new List<ChatPushSubscription>
        {
            NewSub("https://push.example/sub/1"),
            NewSub("https://push.example/sub/2"),
        };
        _subscriptions.ListAsync(Arg.Any<CancellationToken>()).Returns(subs);
        _sender.SendAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IWebPushSender.Result(Sent: true));
        var run = NewRun(conversationId);

        await NewNotifier(pushEnabled: true).RunCompletedAsync(run, CancellationToken.None);

        await _sender.Received(1).SendAsync(
            "https://push.example/sub/1", "p256dh-key", "auth-secret",
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sender.Received(1).SendAsync(
            "https://push.example/sub/2", "p256dh-key", "auth-secret",
            Arg.Any<string>(), Arg.Any<CancellationToken>());

        var captured = _sender.ReceivedCalls()
            .Select(call => (string)call.GetArguments()[3]!)
            .First();
        captured.ShouldContain($"\"runId\":\"{run.Id.Value}\"");
        captured.ShouldContain($"\"conversationId\":\"{conversationId.Value}\"");
        captured.ShouldContain("\"title\":\"Revisao do PR\"");
        captured.ShouldContain("\"status\":\"completed\"");
        captured.ShouldContain($"\"url\":\"/ai-chat?c={conversationId.Value}\"");
    }

    [Fact]
    public async Task Dado_SemSubscriptions_Quando_Notifica_Entao_SaiSemConsultarSender()
    {
        _subscriptions.ListAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ChatPushSubscription>());

        await NewNotifier(pushEnabled: true).RunCompletedAsync(
            NewRun(ChatConversationId.NewGuid()), CancellationToken.None);

        await _sender.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    public async Task Dado_EndpointMorto_Quando_Notifica_Entao_PodaSubscription(int httpStatus)
    {
        var dead = NewSub("https://push.example/sub/dead");
        _subscriptions.ListAsync(Arg.Any<CancellationToken>()).Returns([dead]);
        _sender.SendAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IWebPushSender.Result(Sent: false, HttpStatus: httpStatus, Error: "gone"));

        await NewNotifier(pushEnabled: true).RunCompletedAsync(
            NewRun(ChatConversationId.NewGuid()), CancellationToken.None);

        await _subscriptions.Received(1).DeleteAsync(dead, Arg.Any<CancellationToken>());
        await _subscriptions.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_FalhaTransitoria_Quando_Notifica_Entao_MantemSubscription()
    {
        var sub = NewSub("https://push.example/sub/flaky");
        _subscriptions.ListAsync(Arg.Any<CancellationToken>()).Returns([sub]);
        _sender.SendAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IWebPushSender.Result(Sent: false, HttpStatus: 503, Error: "busy"));

        await NewNotifier(pushEnabled: true).RunCompletedAsync(
            NewRun(ChatConversationId.NewGuid()), CancellationToken.None);

        await _subscriptions.DidNotReceive().DeleteAsync(Arg.Any<ChatPushSubscription>(), Arg.Any<CancellationToken>());
    }
}
