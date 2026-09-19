using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Integrations.Agents;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

public sealed class AcpSessionClientTests
{
    [Fact]
    public void Dado_KnownCliAgentAdapter_Quando_ConsultadoSuporteSessao_Entao_RetornaTrueParaAgentesHabilitados()
    {
        // Covers RF-008: capability
        var adapter = new KnownCliAgentAdapter();
        adapter.SupportsInteractiveSession.ShouldBeTrue();
    }

    [Theory]
    [InlineData(AgentType.OpenCode)]
    [InlineData(AgentType.Claude)]
    [InlineData(AgentType.Codex)]
    public void Dado_AgenteComSuporteASessao_Quando_BuildSessionCommand_Entao_GeraComandoValido(AgentType agentType)
    {
        // Covers T2: matriz de argv por agente
        var adapter = new KnownCliAgentAdapter();
        var cmd = adapter.BuildSessionCommand(agentType, "/tmp/workspace", Sandbox.WorkspaceWrite);

        cmd.ShouldNotBeNull();
        cmd.WorkingDirectory.ShouldBe("/tmp/workspace");
        cmd.Arguments.ShouldNotBeEmpty();
    }

    [Fact]
    public void Dado_NotificationSessionUpdate_Quando_Parseada_Entao_GeraEventoEstruturado()
    {
        // Covers RF-004: parsing de session/update
        var json = """
        {
            "jsonrpc": "2.0",
            "method": "session/update",
            "params": {
                "kind": "tool_call",
                "content": "Running test",
                "tool": "bash",
                "args": "dotnet test"
            }
        }
        """;

        var (method, kind, content, payload) = AcpSessionMessageParser.ParseNotification(json);
        method.ShouldBe("session/update");
        kind.ShouldBe("tool_call");
        content.ShouldBe("Running test");
        payload.ShouldNotBeNull();
    }

    [Fact]
    public void Dado_NotificationRequestPermission_Quando_Parseada_Entao_GeraPermissionRequestInfo()
    {
        // Covers RF-005: parsing de session/request_permission
        var json = """
        {
            "jsonrpc": "2.0",
            "method": "session/request_permission",
            "params": {
                "requestId": "req_123",
                "tool": "bash",
                "detail": "Execute 'rm -rf bin/'",
                "options": ["allow", "deny", "always"]
            }
        }
        """;

        var perm = AcpSessionMessageParser.ParsePermissionRequest(json);
        perm.ShouldNotBeNull();
        perm.RequestId.ShouldBe("req_123");
        perm.Tool.ShouldBe("bash");
        perm.Detail.ShouldBe("Execute 'rm -rf bin/'");
        perm.Options.ShouldContain("allow");
        perm.Options.ShouldContain("deny");
        perm.Options.ShouldContain("always");
    }

    [Fact]
    public async Task Dado_AcpSessionClient_Quando_IniciadoSemAdaptador_Entao_LancaNotSupportedException()
    {
        var client = new AcpSessionClient(Enumerable.Empty<IAgentAdapter>());
        await Should.ThrowAsync<NotSupportedException>(() =>
            client.StartSessionAsync("thread_1", AgentType.OpenCode, "/tmp", Sandbox.WorkspaceWrite));
    }

    [Fact]
    public void Dado_AcpSessionClient_Quando_NaoAtivo_Entao_IsSessionActiveRetornaFalse()
    {
        var client = new AcpSessionClient(Enumerable.Empty<IAgentAdapter>());
        client.IsSessionActive("inexistente").ShouldBeFalse();
    }
}
