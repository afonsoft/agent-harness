using Shouldly;
using Taskboard.Agents;
using Taskboard.Integrations.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20260921-agent-execution-event-pipeline RF-002: parser conforme ao ACP real
/// (<c>session/update</c> com discriminador <c>update.sessionUpdate</c>,
/// <c>session/request_permission</c> como request com id) + tolerância ao shape legado.
/// </summary>
public sealed class AcpProtocolParserTests
{
    [Fact]
    public void Dado_SessionUpdateToolCall_Quando_Parse_Entao_KindToolCallComToolCallId()
    {
        var json = """
        {
            "jsonrpc": "2.0",
            "method": "session/update",
            "params": {
                "sessionId": "sess-1",
                "update": {
                    "sessionUpdate": "tool_call",
                    "toolCallId": "call-42",
                    "title": "dotnet test",
                    "kind": "execute",
                    "status": "in_progress",
                    "rawInput": { "command": "dotnet test" }
                }
            }
        }
        """;

        var parsed = AcpProtocolParser.Parse(json);

        parsed.ShouldNotBeNull();
        parsed!.Type.ShouldBe(AcpProtocolParser.MessageType.Notification);
        parsed.Kind.ShouldBe(AgentEventKinds.ToolCall);
        parsed.ToolCallId.ShouldBe("call-42");
        parsed.SessionId.ShouldBe("sess-1");
        parsed.Content.ShouldBe("dotnet test");
        parsed.PayloadJson.ShouldNotBeNull();
    }

    [Fact]
    public void Dado_ToolCallUpdate_Quando_Parse_Entao_KindToolOutputCorrelacionado()
    {
        var json = """
        {
            "jsonrpc": "2.0",
            "method": "session/update",
            "params": {
                "sessionId": "sess-1",
                "update": {
                    "sessionUpdate": "tool_call_update",
                    "toolCallId": "call-42",
                    "status": "completed",
                    "rawOutput": "all tests passed"
                }
            }
        }
        """;

        var parsed = AcpProtocolParser.Parse(json);

        parsed!.Kind.ShouldBe(AgentEventKinds.ToolOutput);
        parsed.ToolCallId.ShouldBe("call-42");
    }

    [Fact]
    public void Dado_MessageChunk_Quando_Parse_Entao_KindMessageComTexto()
    {
        var json = """
        {
            "jsonrpc": "2.0",
            "method": "session/update",
            "params": {
                "sessionId": "s",
                "update": {
                    "sessionUpdate": "agent_message_chunk",
                    "content": { "type": "text", "text": "Vou editar o arquivo." }
                }
            }
        }
        """;

        var parsed = AcpProtocolParser.Parse(json);

        parsed!.Kind.ShouldBe(AgentEventKinds.Message);
        parsed.Content.ShouldBe("Vou editar o arquivo.");
    }

    [Fact]
    public void Dado_ThoughtChunk_Quando_Parse_Entao_KindThought()
    {
        var json = """
        {
            "jsonrpc": "2.0",
            "method": "session/update",
            "params": {
                "sessionId": "s",
                "update": {
                    "sessionUpdate": "agent_thought_chunk",
                    "content": { "type": "text", "text": "Vou usar o adapter existente." }
                }
            }
        }
        """;

        var parsed = AcpProtocolParser.Parse(json);

        parsed!.Kind.ShouldBe(AgentEventKinds.Thought);
    }

    [Fact]
    public void Dado_PlanUpdate_Quando_Parse_Entao_KindPlan()
    {
        var json = """
        {
            "jsonrpc": "2.0",
            "method": "session/update",
            "params": {
                "sessionId": "s",
                "update": {
                    "sessionUpdate": "plan",
                    "entries": [
                        { "content": "Ler spec", "priority": "high", "status": "completed" },
                        { "content": "Implementar", "priority": "high", "status": "in_progress" }
                    ]
                }
            }
        }
        """;

        var parsed = AcpProtocolParser.Parse(json);

        parsed!.Kind.ShouldBe(AgentEventKinds.Plan);
        parsed!.PayloadJson!.ShouldContain("entries");
    }

    [Fact]
    public void Dado_RequestPermissionAcpReal_Quando_Parse_Entao_RequestComIdEOptions()
    {
        var json = """
        {
            "jsonrpc": "2.0",
            "id": 7,
            "method": "session/request_permission",
            "params": {
                "sessionId": "s",
                "toolCall": { "toolCallId": "c1", "title": "git push", "kind": "execute" },
                "options": [
                    { "optionId": "allow_once", "name": "Allow once", "kind": "allow_once" },
                    { "optionId": "reject_once", "name": "Reject", "kind": "reject_once" }
                ]
            }
        }
        """;

        var parsed = AcpProtocolParser.Parse(json);

        parsed!.Type.ShouldBe(AcpProtocolParser.MessageType.Request);
        parsed.Kind.ShouldBe(AgentEventKinds.Permission);
        parsed.RequestId.ShouldBe("7");
        parsed!.PayloadJson!.ShouldContain("allow_once");
        parsed!.PayloadJson!.ShouldContain("git push");
    }

    [Fact]
    public void Dado_ResponseJsonRpc_Quando_Parse_Entao_TipoResponseComRequestId()
    {
        var json = """{ "jsonrpc": "2.0", "id": "abc123", "result": { "sessionId": "s-9" } }""";

        var parsed = AcpProtocolParser.Parse(json);

        parsed!.Type.ShouldBe(AcpProtocolParser.MessageType.Response);
        parsed.RequestId.ShouldBe("abc123");
        parsed.ResponseResult.GetProperty("sessionId").GetString().ShouldBe("s-9");
    }

    [Fact]
    public void Dado_SessionUpdateDesconhecido_Quando_Parse_Entao_KindActivityComRaw()
    {
        var json = """
        {
            "jsonrpc": "2.0",
            "method": "session/update",
            "params": {
                "sessionId": "s",
                "update": { "sessionUpdate": "future_kind_x", "data": 1 }
            }
        }
        """;

        var parsed = AcpProtocolParser.Parse(json);

        parsed!.Kind.ShouldBe(AgentEventKinds.Activity);
        parsed!.PayloadJson!.ShouldContain("future_kind_x");
    }

    [Fact]
    public void Dado_ShapeLegado_Quando_Parse_Entao_KindDoParams()
    {
        var json = """
        {
            "jsonrpc": "2.0",
            "method": "session/update",
            "params": { "kind": "tool_call", "content": "Running test" }
        }
        """;

        var parsed = AcpProtocolParser.Parse(json);

        parsed!.Kind.ShouldBe("tool_call");
        parsed.Content.ShouldBe("Running test");
    }

    [Fact]
    public void Dado_LinhaNaoJson_Quando_Parse_Entao_Null()
    {
        AcpProtocolParser.Parse("plain stdout line").ShouldBeNull();
    }

    [Fact]
    public void Dado_OutcomeAllow_Quando_MapaOpcao_Entao_EscolheAllowOnce()
    {
        var options = new[] { "allow_once", "allow_always", "reject_once" };

        AcpSessionClient.MapOutcomeToOption("allow", options).ShouldBe("allow_once");
        AcpSessionClient.MapOutcomeToOption("deny", options).ShouldBe("reject_once");
        AcpSessionClient.MapOutcomeToOption("deny", ["allow_once"]).ShouldBeNull();
    }
}
