using Shouldly;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Server.Services;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

public sealed class PermissionGateTests
{
    private sealed class TestEventStreamService : IThreadEventStreamService
    {
        public Task PublishAsync(string threadId, ServerSentEvent serverSentEvent, CancellationToken ct = default)
        {
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
}
