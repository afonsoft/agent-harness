using System.Collections.Concurrent;
using System.Text.Json;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Integrations.Agents;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20260921-acp-v2-readiness acceptance criteria over a loopback TCP
/// agent: negotiated protocolVersion, dialect selection, v2 turn lifecycle
/// and the removed v1 client surface (fs/*, terminal/*, set_mode).
/// </summary>
public sealed class AcpV2ConformanceTests
{
    [Fact]
    public async Task Dado_SemFlag_Quando_StartSession_Entao_InitializeOfereceV1()
    {
        // AC-1: without flags all traffic uses protocolVersion:1.
        await using var agent = new FakeAcpAgent();
        agent.Responder = (id, method, _) => method switch
        {
            "initialize" => """{"protocolVersion":1,"agentCapabilities":{},"authMethods":[]}""",
            "session/new" => """{"sessionId":"s-1"}""",
            _ => "{}"
        };
        await agent.StartAsync();

        var client = new AcpSessionClient([agent.Adapter], new AcpSessionOptions());
        var events = new ConcurrentQueue<AgentSessionEvent>();
        client.RegisterEventListener("t1", events.Enqueue);

        (await client.StartSessionAsync("t1", AgentType.OpenCode, "/tmp", Sandbox.WorkspaceWrite))
            .ShouldBeTrue();

        var init = await agent.WaitForAsync(e =>
            e.TryGetProperty("method", out var m) && m.GetString() == "initialize");
        init.GetProperty("params").GetProperty("protocolVersion").GetInt32().ShouldBe(1);
        // v1 envelope shape: clientInfo + clientCapabilities.
        init.GetProperty("params").TryGetProperty("clientInfo", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_AgenteV1_Quando_MaxVersion2_Entao_FallbackTransparenteParaV1()
    {
        // AC-3: offered 2, a v1-only agent answers 1 → the session proceeds in v1.
        await using var agent = new FakeAcpAgent();
        agent.Responder = (id, method, _) => method switch
        {
            "initialize" => """{"protocolVersion":1,"agentInfo":{"name":"v1-only","version":"1.0"},"agentCapabilities":{},"authMethods":[]}""",
            "session/new" => """{"sessionId":"s-1"}""",
            "session/prompt" => """{"stopReason":"end_turn"}""",
            _ => "{}"
        };
        await agent.StartAsync();

        var client = new AcpSessionClient([agent.Adapter],
            new AcpSessionOptions { MaxProtocolVersion = 2 });
        var events = new ConcurrentQueue<AgentSessionEvent>();
        client.RegisterEventListener("t1", events.Enqueue);

        (await client.StartSessionAsync("t1", AgentType.OpenCode, "/tmp", Sandbox.WorkspaceWrite))
            .ShouldBeTrue();

        // Offer was 2 but the negotiated dialect is v1.
        var init = await agent.WaitForAsync(e =>
            e.TryGetProperty("method", out var m) && m.GetString() == "initialize");
        init.GetProperty("params").GetProperty("protocolVersion").GetInt32().ShouldBe(2);

        // v1 turn semantics: the prompt response itself ends the turn.
        (await client.SendPromptAsync("t1", "hi")).ShouldBeTrue();
        var done = await WaitEventAsync(events,
            e => e.Kind == "session" && e.Content == "Prompt turn completed");
        done.PayloadJson!.ShouldContain("end_turn");
    }

    [Fact]
    public async Task Dado_AgenteV2_Quando_MaxVersion2_Entao_HandshakeAckEUpserts()
    {
        // AC-2: v2 handshake, prompt ack messageId, turn closes on idle
        // state_update, tool calls via upsert — same normalized timeline.
        await using var agent = new FakeAcpAgent();
        agent.Responder = (id, method, _) => method switch
        {
            "initialize" => """
            {
                "protocolVersion": 2,
                "info": { "name": "v2-agent", "title": "V2", "version": "0.9" },
                "capabilities": { "session": { "delete": {}, "mcp": { "stdio": {} } } },
                "authMethods": []
            }
            """,
            "session/new" => """{"sessionId":"s-2","configOptions":[]}""",
            "session/prompt" => """{"messageId":"m-42"}""",
            _ => "{}"
        };
        await agent.StartAsync();

        var client = new AcpSessionClient([agent.Adapter],
            new AcpSessionOptions { MaxProtocolVersion = 2 });
        var events = new ConcurrentQueue<AgentSessionEvent>();
        client.RegisterEventListener("t1", events.Enqueue);

        (await client.StartSessionAsync("t1", AgentType.OpenCode, "/tmp", Sandbox.WorkspaceWrite))
            .ShouldBeTrue();

        var init = await agent.WaitForAsync(e =>
            e.TryGetProperty("method", out var m) && m.GetString() == "initialize");
        init.GetProperty("params").GetProperty("protocolVersion").GetInt32().ShouldBe(2);
        init.GetProperty("params").TryGetProperty("info", out _).ShouldBeTrue();
        init.GetProperty("params").TryGetProperty("clientInfo", out _).ShouldBeFalse();

        (await client.SendPromptAsync("t1", "run the build")).ShouldBeTrue();
        await agent.WaitForAsync(e =>
            e.TryGetProperty("method", out var m) && m.GetString() == "session/prompt");

        // The ack alone must NOT end the turn — idle state_update does.
        await Task.Delay(150);
        events.Any(e => e.Content == "Prompt turn completed").ShouldBeFalse();

        await agent.SendLineAsync("""{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"s-2","update":{"sessionUpdate":"tool_call_update","toolCallId":"tc-1","title":"Build","status":"in_progress"}}}""");
        await agent.SendLineAsync("""{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"s-2","update":{"sessionUpdate":"tool_call_update","toolCallId":"tc-1","status":"completed","rawOutput":"ok"}}}""");
        await agent.SendLineAsync("""{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"s-2","update":{"sessionUpdate":"agent_message_chunk","messageId":"m-42","content":{"type":"text","text":"done"}}}}""");
        await agent.SendLineAsync("""{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"s-2","update":{"sessionUpdate":"state_update","state":"idle","stopReason":"end_turn"}}}""");

        var done = await WaitEventAsync(events,
            e => e.Kind == "session" && e.Content == "Prompt turn completed");
        done.PayloadJson!.ShouldContain("end_turn");
        done.PayloadJson!.ShouldContain("m-42");

        // Upsert: first update → tool_call, patch → tool_output.
        var toolCall = await WaitEventAsync(events, e => e.Kind == "tool_call" && e.ToolCallId == "tc-1");
        toolCall.PatchOp.ShouldBe(AgentPatchOps.Append);
        var toolPatch = await WaitEventAsync(events, e => e.Kind == "tool_output" && e.ToolCallId == "tc-1");
        toolPatch.PatchOp.ShouldBe(AgentPatchOps.Replace);

        var chunk = await WaitEventAsync(events, e => e.Kind == "message" && e.MessageId == "m-42");
        chunk.PatchOp.ShouldBe(AgentPatchOps.Append);
        chunk.Content.ShouldBe("done");

        // v2 negotiated version surfaces in session_info.
        var info = await WaitEventAsync(events, e => e.Kind == "session_info" && e.PayloadJson?.Contains("protocolVersion") == true);
        info.PayloadJson!.ShouldContain("\"protocolVersion\":2");
    }

    [Fact]
    public async Task Dado_ConexaoV2_Quando_AgenteChamaFsRead_Entao_MethodNotFound()
    {
        // AC-4: removed v1 client methods are never served on v2 connections.
        await using var agent = new FakeAcpAgent();
        agent.Responder = (id, method, _) => method switch
        {
            "initialize" => """{"protocolVersion":2,"info":{"name":"v2","version":"1"},"capabilities":{"session":{}},"authMethods":[]}""",
            "session/new" => """{"sessionId":"s-2"}""",
            _ => "{}"
        };
        await agent.StartAsync();

        var dispatched = 0;
        var handler = new CountingToolHandler(() => dispatched++);
        var client = new AcpSessionClient([agent.Adapter],
            new AcpSessionOptions { MaxProtocolVersion = 2 }, handler);
        client.RegisterEventListener("t1", _ => { });

        (await client.StartSessionAsync("t1", AgentType.OpenCode, "/tmp", Sandbox.WorkspaceWrite))
            .ShouldBeTrue();

        // Agent tries the removed fs/* surface — gets -32601, no dispatch.
        await agent.SendLineAsync("""{"jsonrpc":"2.0","id":"req-fs","method":"fs/read_text_file","params":{"path":"/tmp/x"}}""");
        var reply = await agent.WaitForAsync(e =>
            e.TryGetProperty("id", out var i) && i.GetString() == "req-fs"
            && e.TryGetProperty("error", out _));
        reply.GetProperty("error").GetProperty("code").GetInt32().ShouldBe(-32601);
        dispatched.ShouldBe(0);

        // session/set_mode is gone in v2 — never sent on the wire.
        (await client.SetModeAsync("t1", "plan")).ShouldBeFalse();
        agent.ReceivedMessage(e =>
            e.TryGetProperty("method", out var m) && m.GetString() == "session/set_mode").ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_ConexaoV1_Quando_AgenteChamaFsRead_Entao_DespachaNormal()
    {
        // Parity guard: on v1 the fs/* surface still works.
        await using var agent = new FakeAcpAgent();
        agent.Responder = (id, method, _) => method switch
        {
            "initialize" => """{"protocolVersion":1,"agentCapabilities":{},"authMethods":[]}""",
            "session/new" => """{"sessionId":"s-1"}""",
            _ => "{}"
        };
        await agent.StartAsync();

        var dispatched = 0;
        var handler = new CountingToolHandler(() => dispatched++);
        var client = new AcpSessionClient([agent.Adapter],
            new AcpSessionOptions(), handler);
        client.RegisterEventListener("t1", _ => { });

        (await client.StartSessionAsync("t1", AgentType.OpenCode, "/tmp", Sandbox.WorkspaceWrite))
            .ShouldBeTrue();

        await agent.SendLineAsync("""{"jsonrpc":"2.0","id":"req-fs","method":"fs/read_text_file","params":{"path":"/tmp/x"}}""");
        var reply = await agent.WaitForAsync(e =>
            e.TryGetProperty("id", out var i) && i.GetString() == "req-fs");
        reply.GetProperty("result").GetRawText().ShouldContain("handled");
        dispatched.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_AgenteV3_Quando_MaxVersion2_Entao_UnsupportedVersion()
    {
        // An agent answering above our offer fails cleanly with a typed event.
        await using var agent = new FakeAcpAgent();
        agent.Responder = (id, method, _) => method switch
        {
            "initialize" => """{"protocolVersion":3,"info":{"name":"future","version":"9"},"capabilities":{},"authMethods":[]}""",
            _ => "{}"
        };
        await agent.StartAsync();

        var client = new AcpSessionClient([agent.Adapter],
            new AcpSessionOptions { MaxProtocolVersion = 2 });
        var events = new ConcurrentQueue<AgentSessionEvent>();
        client.RegisterEventListener("t1", events.Enqueue);

        (await client.StartSessionAsync("t1", AgentType.OpenCode, "/tmp", Sandbox.WorkspaceWrite))
            .ShouldBeFalse();

        var err = await WaitEventAsync(events, e => e.Kind == "error");
        err.PayloadJson!.ShouldContain("unsupported_version");
    }

    [Fact]
    public async Task Dado_LinhaBatchNdjson_Quando_Session_Entao_ProcessaCadaEntrada()
    {
        // RF-208: batch arrays are tolerated — each entry is dispatched and
        // invalid entries get a per-entry -32600 response.
        await using var agent = new FakeAcpAgent();
        agent.Responder = (id, method, _) => method switch
        {
            "initialize" => """{"protocolVersion":2,"info":{"name":"v2","version":"1"},"capabilities":{"session":{}},"authMethods":[]}""",
            "session/new" => """{"sessionId":"s-2"}""",
            _ => "{}"
        };
        await agent.StartAsync();

        var client = new AcpSessionClient([agent.Adapter],
            new AcpSessionOptions { MaxProtocolVersion = 2 });
        var events = new ConcurrentQueue<AgentSessionEvent>();
        client.RegisterEventListener("t1", events.Enqueue);

        (await client.StartSessionAsync("t1", AgentType.OpenCode, "/tmp", Sandbox.WorkspaceWrite))
            .ShouldBeTrue();

        await agent.SendLineAsync(
            """[{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"s-2","update":{"sessionUpdate":"agent_message_chunk","messageId":"m-9","content":{"type":"text","text":"batch"}}}},{"jsonrpc":"2.0","id":"b-fs","method":"fs/read_text_file","params":{}},42]""");

        var chunk = await WaitEventAsync(events, e => e.Kind == "message" && e.MessageId == "m-9");
        chunk.Content.ShouldBe("batch");

        // The removed fs/* entry gets -32601, the scalar entry -32600.
        var fsReply = await agent.WaitForAsync(e =>
            e.TryGetProperty("id", out var i) && i.GetString() == "b-fs" && e.TryGetProperty("error", out _));
        fsReply.GetProperty("error").GetProperty("code").GetInt32().ShouldBe(-32601);
        var invalidReply = await agent.WaitForAsync(e =>
            e.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.Null && e.TryGetProperty("error", out _));
        invalidReply.GetProperty("error").GetProperty("code").GetInt32().ShouldBe(-32600);
    }

    private static async Task<AgentSessionEvent> WaitEventAsync(
        ConcurrentQueue<AgentSessionEvent> events, Func<AgentSessionEvent, bool> match)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!cts.IsCancellationRequested)
        {
            if (events.FirstOrDefault(e => match(e)) is { } found)
            {
                return found;
            }

            await Task.Delay(20, cts.Token).ContinueWith(_ => { });
        }

        throw new TimeoutException("Expected session event never arrived. Got: "
            + string.Join(" | ", events.Select(e => $"{e.Kind}/{e.Role}/{e.MessageId}/{e.ToolCallId}/{e.PatchOp}:{e.Content}")));
    }

    private sealed class CountingToolHandler(Action onCall) : IAcpClientToolHandler
    {
        public Task<JsonElement> HandleAsync(
            string threadId, string sessionId, string workspacePath,
            string method, JsonElement p, CancellationToken cancellationToken)
        {
            onCall();
            return Task.FromResult(JsonSerializer.SerializeToElement(new { handled = method }));
        }
    }
}
