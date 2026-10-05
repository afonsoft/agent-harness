using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using Shouldly;
using Taskboard.Domain.Entities.Chat;
using Taskboard.Repositories;
using Taskboard.Server.Hubs;
using Taskboard.Server.Services;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261005-chat-background-resume RF-008: o notifier publica
/// <c>run.completed</c> com título + status para todos os clients do hub.
/// </summary>
public class SignalRChatRunNotifierTests
{
    private readonly IHubContext<ChatRunHub> _hub = Substitute.For<IHubContext<ChatRunHub>>();
    private readonly IHubClients _clients = Substitute.For<IHubClients>();
    private readonly IClientProxy _all = Substitute.For<IClientProxy>();
    private readonly IRepository<ChatConversation> _conversations = Substitute.For<IRepository<ChatConversation>>();

    public SignalRChatRunNotifierTests()
    {
        _hub.Clients.Returns(_clients);
        _clients.All.Returns(_all);
    }

    private SignalRChatRunNotifier NewNotifier() => new(_hub, _conversations);

    private static ChatRun NewRun(ChatConversationId conversationId)
    {
        var run = ChatRun.Create(
            ChatRunId.NewGuid(), conversationId, ChatMessageId.NewGuid());
        run.Start();
        run.Complete(tokensIn: 10, tokensOut: 20);
        return run;
    }

    [Fact]
    public async Task Dado_RunConcluido_Quando_Notifica_Entao_EnviaRunCompletedComTituloEStatus()
    {
        var conversationId = ChatConversationId.NewGuid();
        var conversation = ChatConversation.Create(
            conversationId, Guid.NewGuid(), "fake", "model", title: "Revisao do PR");
        _conversations.GetAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(conversation);
        var run = NewRun(conversationId);
        object?[]? captured = null;
        _all.SendCoreAsync(
                Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => captured = call.Arg<object?[]>());

        await NewNotifier().RunCompletedAsync(run, CancellationToken.None);

        await _all.Received(1).SendCoreAsync(
            SignalRChatRunNotifier.RunCompletedEvent,
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
        captured.ShouldNotBeNull();
        var payload = System.Text.Json.JsonSerializer.Serialize(captured![0]);
        payload.ShouldContain($"\"runId\":\"{run.Id.Value}\"");
        payload.ShouldContain($"\"conversationId\":\"{conversationId.Value}\"");
        payload.ShouldContain("\"title\":\"Revisao do PR\"");
        payload.ShouldContain("\"status\":\"completed\"");
    }

    [Fact]
    public async Task Dado_RunFalhou_Quando_Notifica_Entao_PayloadLevaStatusEError()
    {
        var conversationId = ChatConversationId.NewGuid();
        _conversations.GetAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns((ChatConversation?)null);
        var run = ChatRun.Create(
            ChatRunId.NewGuid(), conversationId, ChatMessageId.NewGuid());
        run.Start();
        run.Fail("provider refused");
        object?[]? captured = null;
        _all.SendCoreAsync(
                Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => captured = call.Arg<object?[]>());

        await NewNotifier().RunCompletedAsync(run, CancellationToken.None);

        captured.ShouldNotBeNull();
        var payload = System.Text.Json.JsonSerializer.Serialize(captured![0]);
        payload.ShouldContain("\"status\":\"failed\"");
        payload.ShouldContain("provider refused");
    }
}
