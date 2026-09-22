using System.Text.Json;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Integrations.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20260921-acp-v2-readiness RF-201/205: v2 dialect surface — handshake
/// shape, session update taxonomy, upsert keys/ops, permission shape and the
/// removed v1 client surface.
/// </summary>
public sealed class AcpV2DialectTests
{
    private static readonly IAcpDialect V2 = AcpDialects.V2;

    private static AcpProtocolParser.Parsed ParseUpdate(string updateJson)
    {
        var line = """{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"s-1","update":"""
                   + updateJson + "}}";
        var parsed = AcpProtocolParser.Parse(line, V2);
        parsed.ShouldNotBeNull();
        return parsed;
    }

    [Fact]
    public void Dado_V2_Quando_BuildInitializeParams_Entao_InfoECapabilitiesSemFsTerminal()
    {
        var p = JsonSerializer.SerializeToElement(V2.BuildInitializeParams(new AcpSessionOptions()));

        p.GetProperty("protocolVersion").GetInt32().ShouldBe(2);
        p.GetProperty("info").GetProperty("name").GetString().ShouldBe("taskboard");
        p.TryGetProperty("clientInfo", out _).ShouldBeFalse();
        p.TryGetProperty("clientCapabilities", out _).ShouldBeFalse();
    }

    [Fact]
    public void Dado_V2InitializeResult_Quando_Parse_Entao_CapturaCapabilitiesSession()
    {
        var json = """
        {
            "protocolVersion": 2,
            "info": { "name": "agent-v2", "title": "A2", "version": "2.0" },
            "capabilities": {
                "session": {
                    "delete": {},
                    "additionalDirectories": {},
                    "prompt": { "image": {}, "embeddedContext": {} },
                    "mcp": { "stdio": {}, "http": {} }
                }
            },
            "authMethods": [
                { "methodId": "oauth", "type": "agent", "name": "OAuth" }
            ]
        }
        """;

        var peer = V2.ParseInitializeResult(JsonDocument.Parse(json).RootElement);

        peer.ProtocolVersion.ShouldBe(2);
        peer.AgentName.ShouldBe("agent-v2");
        peer.LoadSession.ShouldBeFalse(); // removed in v2
        peer.SessionResume.ShouldBeTrue(); // baseline
        peer.SessionClose.ShouldBeTrue();
        peer.SessionList.ShouldBeTrue();
        peer.SessionDelete.ShouldBeTrue();
        peer.AdditionalDirectories.ShouldBeTrue();
        peer.PromptImage.ShouldBeTrue();
        peer.PromptAudio.ShouldBeFalse();
        peer.McpStdio.ShouldBeTrue();
        peer.McpHttp.ShouldBeTrue();
        peer.AuthLogout.ShouldBeTrue(); // implied by non-empty authMethods
        peer.AuthMethods[0].Id.ShouldBe("oauth");
    }

    [Fact]
    public void Dado_V2AgentMessage_Quando_Parse_Entao_MessageComMessageIdEReplace()
    {
        var parsed = ParseUpdate("""
        {"sessionUpdate":"agent_message","messageId":"m-1","content":[{"type":"text","text":"hello"},{"type":"text","text":" world"}]}
        """);

        parsed.Kind.ShouldBe(AgentEventKinds.Message);
        parsed.MessageId.ShouldBe("m-1");
        parsed.PatchOp.ShouldBe(AgentPatchOps.Replace);
        parsed.Content.ShouldBe("hello world");
    }

    [Fact]
    public void Dado_V2AgentMessageContentNull_Quando_Parse_Entao_Clear()
    {
        var parsed = ParseUpdate("""{"sessionUpdate":"agent_message","messageId":"m-1","content":null}""");

        parsed.Kind.ShouldBe(AgentEventKinds.Message);
        parsed.PatchOp.ShouldBe(AgentPatchOps.Clear);
    }

    [Fact]
    public void Dado_V2AgentMessageSemContent_Quando_Parse_Entao_ReplaceSemTexto()
    {
        // Omitted content = metadata-only update — replace merge keeps prior content.
        var parsed = ParseUpdate("""{"sessionUpdate":"agent_message","messageId":"m-1"}""");

        parsed.PatchOp.ShouldBe(AgentPatchOps.Replace);
        parsed.Content.ShouldBeNull();
    }

    [Fact]
    public void Dado_V2Chunks_Quando_Parse_Entao_AppendComMessageIdERole()
    {
        var chunk = ParseUpdate("""
        {"sessionUpdate":"agent_thought_chunk","messageId":"m-7","content":{"type":"text","text":"thinking"}}
        """);

        chunk.Kind.ShouldBe(AgentEventKinds.Thought);
        chunk.MessageId.ShouldBe("m-7");
        chunk.PatchOp.ShouldBe(AgentPatchOps.Append);

        var userChunk = ParseUpdate("""
        {"sessionUpdate":"user_message_chunk","messageId":"u-1","content":{"type":"text","text":"hi"}}
        """);
        userChunk.Role.ShouldBe("user");
    }

    [Fact]
    public void Dado_V2StateUpdate_Quando_Parse_Entao_LifecycleComState()
    {
        var parsed = ParseUpdate("""{"sessionUpdate":"state_update","state":"running"}""");

        parsed.Kind.ShouldBe(AgentEventKinds.Lifecycle);
        parsed.Content.ShouldBe("running");
    }

    [Fact]
    public void Dado_V2StateUpdateIdle_Quando_TryCompleteTurn_Entao_EncerraComStopReason()
    {
        var tracker = new AcpV2TurnTracker();
        tracker.PromptResponseEndsTurn.ShouldBeFalse();

        var updateParams = JsonDocument.Parse("""
        {"sessionId":"s-1","update":{"sessionUpdate":"state_update","state":"idle","stopReason":"end_turn"}}
        """).RootElement;

        tracker.TryCompleteTurn(updateParams, out var stopReason).ShouldBeTrue();
        stopReason.ShouldBe("end_turn");

        var running = JsonDocument.Parse("""
        {"sessionId":"s-1","update":{"sessionUpdate":"state_update","state":"running"}}
        """).RootElement;
        tracker.TryCompleteTurn(running, out _).ShouldBeFalse();
    }

    [Fact]
    public void Dado_V2PromptAck_Quando_OnPromptResponse_Entao_CapturaMessageId()
    {
        var tracker = new AcpV2TurnTracker();
        tracker.OnPromptResponse(JsonDocument.Parse("""{"messageId":"m-9"}""").RootElement);
        tracker.MessageId.ShouldBe("m-9");
    }

    [Fact]
    public void Dado_V2ToolCallUpdate_Quando_Parse_Entao_MarcadoComoUpsert()
    {
        var parsed = ParseUpdate("""
        {"sessionUpdate":"tool_call_update","toolCallId":"tc-1","title":"Read file","status":"in_progress"}
        """);

        parsed.IsToolCallUpsert.ShouldBeTrue();
        parsed.ToolCallId.ShouldBe("tc-1");
        parsed.PatchOp.ShouldBe(AgentPatchOps.Replace);
    }

    [Fact]
    public void Dado_V2ToolCallContentChunk_Quando_Parse_Entao_ToolOutputAppend()
    {
        var parsed = ParseUpdate("""
        {"sessionUpdate":"tool_call_content_chunk","toolCallId":"tc-1","content":{"type":"text","text":"partial"}}
        """);

        parsed.Kind.ShouldBe(AgentEventKinds.ToolOutput);
        parsed.PatchOp.ShouldBe(AgentPatchOps.Append);
        parsed.Content.ShouldBe("partial");
    }

    [Fact]
    public void Dado_V2PlanUpdate_Quando_Parse_Entao_PlanComPlanIdEEntries()
    {
        var parsed = ParseUpdate("""
        {"sessionUpdate":"plan_update","plan":{"type":"items","planId":"p-1","entries":[{"content":"step 1","status":"in_progress"}]}}
        """);

        parsed.Kind.ShouldBe(AgentEventKinds.Plan);
        parsed.PlanId.ShouldBe("p-1");
        parsed.PatchOp.ShouldBe(AgentPatchOps.Replace);
        // entries hoisted so the existing plan card renders v2 payloads.
        using var doc = JsonDocument.Parse(parsed.PayloadJson!);
        doc.RootElement.GetProperty("entries").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public void Dado_V2TerminalOutputChunk_Quando_Parse_Entao_DecodificaBase64()
    {
        var data = Convert.ToBase64String("hello terminal"u8.ToArray());
        var parsed = ParseUpdate(
            "{\"sessionUpdate\":\"terminal_output_chunk\",\"terminalId\":\"term-1\",\"output\":{\"data\":\""
            + data + "\"}}");

        parsed.Kind.ShouldBe(AgentEventKinds.Terminal);
        parsed.Content.ShouldBe("hello terminal");
    }

    [Fact]
    public void Dado_V2PermissionTitleSubject_Quando_Parse_Entao_Normaliza()
    {
        var line = """
        {"jsonrpc":"2.0","id":"r-1","method":"session/request_permission","params":{"sessionId":"s-1","title":"Run tests","subject":"dotnet test","options":[{"optionId":"allow","name":"Allow","kind":"allow_once"}]}}
        """;

        var parsed = AcpProtocolParser.Parse(line, V2)!;

        parsed.Type.ShouldBe(AcpProtocolParser.MessageType.Request);
        parsed.Kind.ShouldBe(AgentEventKinds.Permission);
        using var doc = JsonDocument.Parse(parsed.PayloadJson!);
        doc.RootElement.GetProperty("tool").GetString().ShouldBe("Run tests");
        doc.RootElement.GetProperty("detail").GetString().ShouldBe("dotnet test");
        doc.RootElement.GetProperty("options")[0].GetString().ShouldBe("allow");
    }

    [Fact]
    public void Dado_V2UpdateDesconhecido_Quando_Parse_Entao_ActivityPreservaRaw()
    {
        var parsed = ParseUpdate("""{"sessionUpdate":"_vendor_extension","foo":42}""");

        parsed.Kind.ShouldBe(AgentEventKinds.Activity);
        parsed.PayloadJson!.ShouldContain("_vendor_extension");
    }

    [Fact]
    public void Dado_V2_Quando_SetConfigOption_Entao_TypeDiscriminatorObrigatorio()
    {
        var p = JsonSerializer.SerializeToElement(
            V2.BuildSetConfigOptionParams("s-1", "model", "claude-opus", isBoolean: false));
        p.GetProperty("type").GetString().ShouldBe("id");

        var b = JsonSerializer.SerializeToElement(
            V2.BuildSetConfigOptionParams("s-1", "fast", "true", isBoolean: true));
        b.GetProperty("type").GetString().ShouldBe("boolean");
        b.GetProperty("value").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Dado_V2_Quando_Surface_Entao_MetodosRemovidosBloqueados()
    {
        V2.SetModeMethod.ShouldBeNull();
        V2.AllowsClientMethod("fs/read_text_file").ShouldBeFalse();
        V2.AllowsClientMethod("fs/write_text_file").ShouldBeFalse();
        V2.AllowsClientMethod("terminal/create").ShouldBeFalse();
        V2.AllowsClientMethod("elicitation/select").ShouldBeTrue();
        V2.AuthenticateMethod.ShouldBe("auth/login");
        V2.LogoutMethod.ShouldBe("auth/logout");
    }

    [Fact]
    public void Dado_V2_Quando_SessionNew_Entao_McpServersOpcional()
    {
        var peer = new AcpPeerInfo();
        var empty = JsonSerializer.SerializeToElement(V2.BuildSessionNewParams(peer, "/repo", []));
        empty.TryGetProperty("mcpServers", out _).ShouldBeFalse();

        var withServer = JsonSerializer.SerializeToElement(V2.BuildSessionNewParams(peer, "/repo",
            [new { name = "k", command = "kc", args = Array.Empty<string>() }]));
        withServer.GetProperty("mcpServers").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public void Dado_V2_Quando_Reattach_Entao_ResumeComReplayFrom()
    {
        var peer = new AcpPeerInfo { SessionResume = true };
        var req = V2.BuildReattachRequest(peer, "s-old", "/repo", []);

        req.ShouldNotBeNull();
        req.Method.ShouldBe("session/resume");
        req.ExpectsReplay.ShouldBeTrue();
        var p = JsonSerializer.SerializeToElement(req.Params);
        p.GetProperty("replayFrom").GetProperty("type").GetString().ShouldBe("start");
    }

    [Fact]
    public void Dado_V1_Quando_Parse_Entao_SemPatchOpNemIds()
    {
        // v1 parity: no upsert fields are ever emitted.
        var line = """{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"s-1","update":{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"oi"}}}}""";
        var parsed = AcpProtocolParser.Parse(line)!; // default dialect = v1

        parsed.Kind.ShouldBe(AgentEventKinds.Message);
        parsed.PatchOp.ShouldBeNull();
        parsed.MessageId.ShouldBeNull();
        parsed.PlanId.ShouldBeNull();
    }

    [Fact]
    public void Dado_V1_Quando_Surface_Entao_TudoPermitido()
    {
        var v1 = AcpDialects.V1;
        v1.SetModeMethod.ShouldBe("session/set_mode");
        v1.AllowsClientMethod("fs/read_text_file").ShouldBeTrue();
        v1.AllowsClientMethod("terminal/create").ShouldBeTrue();
        v1.AuthenticateMethod.ShouldBe("authenticate");
    }

    [Fact]
    public void Dado_Versao_Quando_AcpDialectsFor_Entao_SelecionaCorreto()
    {
        AcpDialects.For(1).ShouldBeSameAs(AcpDialects.V1);
        AcpDialects.For(2).ShouldBeSameAs(AcpDialects.V2);
        AcpDialects.For(0).ShouldBeNull();
        AcpDialects.For(3).ShouldBeNull();
    }
}
