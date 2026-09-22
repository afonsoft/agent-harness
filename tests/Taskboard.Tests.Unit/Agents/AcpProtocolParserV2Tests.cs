using System.Text.Json;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Integrations.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20260921-acp-v2-readiness RF-205/RF-206/RF-208: parser ACP v2 —
/// discriminantes novos, envelope upsert (messageId/planId/patchOp/entityKind),
/// forward compatibility (desconhecido vira evento genérico com raw preservado)
/// e estrito nas variantes conhecidas (campo obrigatório ausente = parse error).
/// </summary>
public sealed class AcpProtocolParserV2Tests
{
    private static string Update(string updateJson) =>
        """{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"s-1","update":""" + updateJson + "}}";

    [Fact]
    public void Dado_AgentMessageChunkV2_Quando_Parse_Entao_MessageComMessageId()
    {
        var line = Update("""{"sessionUpdate":"agent_message_chunk","messageId":"m-1","content":{"type":"text","text":"olá"}}""");

        var parsed = AcpProtocolParser.Parse(line, protocolVersion: 2)!;

        parsed.Kind.ShouldBe(AgentEventKinds.Message);
        parsed.Content.ShouldBe("olá");
        parsed.MessageId.ShouldBe("m-1");
        parsed.PatchOp.ShouldBe("append");
        parsed.EntityKind.ShouldBe("message");
        parsed.SessionId.ShouldBe("s-1");
    }

    [Fact]
    public void Dado_AgentMessageChunkSemMessageId_Quando_Parse_Entao_ParseError()
    {
        var line = Update("""{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"x"}}""");

        var parsed = AcpProtocolParser.Parse(line, protocolVersion: 2)!;

        parsed.Kind.ShouldBe(AgentEventKinds.Error);
        parsed.Content!.ShouldContain("messageId");
    }

    [Fact]
    public void Dado_AgentMessageInteira_Quando_Parse_Entao_TextoConcatenadoEUpsert()
    {
        var line = Update(
            """{"sessionUpdate":"agent_message","messageId":"m-9","content":[{"type":"text","text":"a"},{"type":"text","text":"b"}]}""");

        var parsed = AcpProtocolParser.Parse(line, protocolVersion: 2)!;

        parsed.Kind.ShouldBe(AgentEventKinds.Message);
        parsed.Content.ShouldBe("ab");
        parsed.PatchOp.ShouldBe("upsert");
        parsed.MessageId.ShouldBe("m-9");
    }

    [Fact]
    public void Dado_StateUpdateIdle_Quando_Parse_Entao_KindStateComStopReason()
    {
        var line = Update("""{"sessionUpdate":"state_update","state":"idle","stopReason":"end_turn"}""");

        var parsed = AcpProtocolParser.Parse(line, protocolVersion: 2)!;

        parsed.Kind.ShouldBe(AgentEventKinds.State);
        parsed.PayloadJson!.ShouldContain("end_turn");
    }

    [Fact]
    public void Dado_ToolCallUpdateV2_Quando_Parse_Entao_UpsertToolCall()
    {
        var line = Update(
            """{"sessionUpdate":"tool_call_update","toolCallId":"tc-1","title":"Edit","status":"in_progress"}""");

        var parsed = AcpProtocolParser.Parse(line, protocolVersion: 2)!;

        parsed.Kind.ShouldBe(AgentEventKinds.ToolOutput);
        parsed.ToolCallId.ShouldBe("tc-1");
        parsed.PatchOp.ShouldBe("upsert");
        parsed.EntityKind.ShouldBe("tool_call");
        parsed.Content.ShouldBe("Edit");
    }

    [Fact]
    public void Dado_ToolCallUpdateSemId_Quando_Parse_Entao_ParseError()
    {
        var line = Update("""{"sessionUpdate":"tool_call_update","title":"x"}""");

        var parsed = AcpProtocolParser.Parse(line, protocolVersion: 2)!;

        parsed.Kind.ShouldBe(AgentEventKinds.Error);
        parsed.Content!.ShouldContain("toolCallId");
    }

    [Fact]
    public void Dado_ToolCallContentChunk_Quando_Parse_Entao_Append()
    {
        var line = Update(
            """{"sessionUpdate":"tool_call_content_chunk","toolCallId":"tc-1","content":{"type":"content","content":{"type":"text","text":"out"}}}""");

        var parsed = AcpProtocolParser.Parse(line, protocolVersion: 2)!;

        parsed.PatchOp.ShouldBe("append");
        parsed.EntityKind.ShouldBe("tool_call");
        parsed.ToolCallId.ShouldBe("tc-1");
    }

    [Fact]
    public void Dado_PlanUpdate_Quando_Parse_Entao_PlanComPlanId()
    {
        var line = Update(
            """{"sessionUpdate":"plan_update","type":"items","planId":"p-1","entries":[{"content":"do","priority":"high","status":"pending"}]}""");

        var parsed = AcpProtocolParser.Parse(line, protocolVersion: 2)!;

        parsed.Kind.ShouldBe(AgentEventKinds.Plan);
        parsed.PlanId.ShouldBe("p-1");
        parsed.PatchOp.ShouldBe("upsert");
        parsed.EntityKind.ShouldBe("plan");
    }

    [Fact]
    public void Dado_TerminalUpdate_Quando_Parse_Entao_OutputDisplayOnly()
    {
        var line = Update(
            """{"sessionUpdate":"terminal_update","terminalId":"t-1","command":"ls","output":"a.txt"}""");

        var parsed = AcpProtocolParser.Parse(line, protocolVersion: 2)!;

        parsed.Kind.ShouldBe(AgentEventKinds.Output);
        parsed.EntityKind.ShouldBe("terminal");
    }

    [Fact]
    public void Dado_UsageEConfig_Quando_Parse_Entao_KindsCompativeis()
    {
        var usage = AcpProtocolParser.Parse(
            Update("""{"sessionUpdate":"usage_update","used":10,"size":100}"""), 2)!;
        var config = AcpProtocolParser.Parse(
            Update("""{"sessionUpdate":"config_option_update","configOptions":[]}"""), 2)!;
        var commands = AcpProtocolParser.Parse(
            Update("""{"sessionUpdate":"available_commands_update","availableCommands":[]}"""), 2)!;

        usage.Kind.ShouldBe(AgentEventKinds.Metric);
        config.Kind.ShouldBe(AgentEventKinds.SessionInfo);
        commands.Kind.ShouldBe(AgentEventKinds.Commands);
    }

    [Fact]
    public void Dado_DiscriminanteDesconhecido_Quando_Parse_Entao_ActivityRawPreservado()
    {
        // RF-208: variantes futuras nunca quebram o parser — raw vai inteiro.
        var line = Update(
            """{"sessionUpdate":"future_widget","widgetId":"w-1","_meta":{"traceparent":"00-abc"},"_customField":42}""");

        var parsed = AcpProtocolParser.Parse(line, protocolVersion: 2)!;

        parsed.Kind.ShouldBe(AgentEventKinds.Activity);
        parsed.PayloadJson!.ShouldContain("future_widget");
        parsed.PayloadJson!.ShouldContain("traceparent");
        parsed.PayloadJson!.ShouldContain("_customField");
    }

    [Fact]
    public void Dado_MesmoUpdate_Quando_ParseV1vsV2_Entao_V1NaoExtraiUpsertFields()
    {
        var line = Update("""{"sessionUpdate":"agent_message_chunk","messageId":"m-1","content":{"type":"text","text":"hi"}}""");

        var v1 = AcpProtocolParser.Parse(line, protocolVersion: 1)!;
        var v2 = AcpProtocolParser.Parse(line, protocolVersion: 2)!;

        v1.Kind.ShouldBe(AgentEventKinds.Message);
        v1.MessageId.ShouldBeNull();
        v1.PatchOp.ShouldBeNull();
        v2.MessageId.ShouldBe("m-1");
    }

    [Fact]
    public void Dado_PermissionV2ComSubject_Quando_Parse_Entao_Normalizado()
    {
        var line = """
        {"jsonrpc":"2.0","id":"req-7","method":"session/request_permission","params":{
            "sessionId":"s-1",
            "title":"Permitir escrita?",
            "description":"O agente quer escrever arquivo.txt",
            "subject":{"type":"tool_call","toolCall":{"toolCallId":"tc-1","kind":"edit","title":"Edit arquivo.txt"}},
            "options":[{"optionId":"allow_once","name":"Allow","kind":"allow_once"},
                       {"optionId":"reject_once","name":"Reject","kind":"reject_once"}]
        }}
        """;

        var parsed = AcpProtocolParser.Parse(line, protocolVersion: 2)!;

        parsed.Type.ShouldBe(AcpProtocolParser.MessageType.Request);
        parsed.Kind.ShouldBe(AgentEventKinds.Permission);
        using var doc = JsonDocument.Parse(parsed.PayloadJson!);
        doc.RootElement.GetProperty("tool").GetString().ShouldBe("Permitir escrita?");
        doc.RootElement.GetProperty("requestId").GetString().ShouldBe("req-7");
        doc.RootElement.GetProperty("options").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public void Dado_PermissionV2SemTitleComSubjectCommand_Quando_Parse_Entao_DetailDoSubject()
    {
        var line = """
        {"jsonrpc":"2.0","id":"req-8","method":"session/request_permission","params":{
            "sessionId":"s-1",
            "subject":{"type":"command","command":{"command":"rm","cwd":"/tmp"}},
            "options":[]
        }}
        """;

        var parsed = AcpProtocolParser.Parse(line, protocolVersion: 2)!;

        parsed.Kind.ShouldBe(AgentEventKinds.Permission);
        using var doc = JsonDocument.Parse(parsed.PayloadJson!);
        doc.RootElement.GetProperty("detail").GetString()!.ShouldContain("command");
        doc.RootElement.GetProperty("options").GetArrayLength().ShouldBe(2); // fallback allow/deny
    }
}
