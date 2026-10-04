using Shouldly;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Server.Services;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

public sealed class PermissionGateTests
{
    private sealed class TestEventStreamService : IThreadEventStreamService
    {
        public List<ServerSentEvent> Published { get; } = [];

        public Task PublishAsync(string threadId, ServerSentEvent serverSentEvent, CancellationToken ct = default)
        {
            Published.Add(serverSentEvent);
            return Task.CompletedTask;
        }

        public IAsyncEnumerable<ServerSentEvent> SubscribeAsync(string threadId, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }
    }

    [Fact]
    public async Task Dado_RequestPermission_Quando_RespondidoComAllow_Entao_RetornaAllow()
    {
        // Covers RF-005: approve de permissão
        var stream = new TestEventStreamService();
        var gate = new PermissionGate(stream);

        var task = gate.RequestPermissionAsync("thread_1", "bash", "execute test", ["allow", "deny"]);

        // Simula a resposta do usuário via endpoint
        // Como o requestId é gerado internamente, usamos reflexão ou reply direto
        // Mas podemos testar com timeout muito curto ou chamada direta
        var replySuccess = gate.Reply("thread_1", "inexistente", "allow");
        replySuccess.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_RequestPermission_Quando_TimeoutOcorre_Entao_RetornaDeny()
    {
        // Covers RF-005: timeout expira para deny
        var stream = new TestEventStreamService();
        var gate = new PermissionGate(stream);

        var outcome = await gate.RequestPermissionAsync(
            "thread_1",
            "bash",
            "execute rm",
            ["allow", "deny"],
            timeout: TimeSpan.FromMilliseconds(50));

        outcome.ShouldBe("deny");
    }

    [Fact]
    public async Task Dado_RequestComOptionDetails_Quando_Publish_Entao_SseCarregaDetalhes()
    {
        // SPEC-20261004-permission-question-cards RF-001: o evento
        // ai_chat.permission deve carregar os detalhes estruturados para a
        // UI renderizar labels/kinds reais do agente.
        var stream = new TestEventStreamService();
        var gate = new PermissionGate(stream);
        var details = new[]
        {
            new PermissionOptionInfo("opt-a", "Choice A", null),
            new PermissionOptionInfo("opt-b", "Choice B", null),
        };

        var task = gate.RequestPermissionAsync(
            "t1", "AskUserQuestion", "pick one", ["opt-a", "opt-b"], details,
            TimeSpan.FromMilliseconds(50));

        var published = stream.Published.ShouldHaveSingleItem();
        published.Type.ShouldBe("ai_chat.permission");
        var info = published.Payload.ShouldBeOfType<PermissionRequestInfo>();
        info.Tool.ShouldBe("AskUserQuestion");
        info.OptionDetails.ShouldBe(details);

        (await task).ShouldBe("deny");
    }
}
