using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Integrations.Agents;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20260921-acp-v2-readiness: conformidade lado a lado v1/v2 sobre um
/// fake agent ACP em TCP loopback — negociação de versão por conexão,
/// lifecycle de turno v2 (ack + state_update), upserts, batch NDJSON e
/// isolamento dos métodos removidos (fs/*, session/set_mode).
/// </summary>
public sealed class AcpSessionClientV2Tests
{
    private const string ThreadId = "thread-v2";

    private static readonly JsonElement V2InitializeResult = JsonDocument.Parse("""
        {
            "protocolVersion": 2,
            "info": { "name": "fake-acp", "version": "2.0.0" },
            "capabilities": { "session": { "delete": {}, "prompt": { "image": {} }, "mcp": { "stdio": {} } } }
        }
        """).RootElement.Clone();

    private static readonly JsonElement V1InitializeResult = JsonDocument.Parse("""
        {
            "protocolVersion": 1,
            "agentInfo": { "name": "fake-acp", "version": "1.0.0" },
            "agentCapabilities": { "sessionCapabilities": { "resume": {}, "close": {} } },
            "authMethods": []
        }
        """).RootElement.Clone();

    private static (AcpSessionClient Client, ConcurrentQueue<AgentSessionEvent> Events, FakeAcpServer Server)
        Create(int maxProtocolVersion = 2, JsonElement? initializeResult = null)
    {
        var server = new FakeAcpServer();
        var init = initializeResult ?? V2InitializeResult;
        server.Responder = (method, _) => method switch
        {
            "initialize" => init,
            "session/new" => JsonDocument.Parse("""{"sessionId":"s-v2"}""").RootElement.Clone(),
            "session/resume" => JsonDocument.Parse("""{"sessionId":"s-v2"}""").RootElement.Clone(),
            "session/close" => JsonDocument.Parse("{}").RootElement.Clone(),
            "session/prompt" => JsonDocument.Parse("""{"messageId":"msg-1"}""").RootElement.Clone(),
            "session/set_config_option" => JsonDocument.Parse("{}").RootElement.Clone(),
            _ => null
        };

        var adapter = Substitute.For<IAgentAdapter>();
        adapter.CanHandle(Arg.Any<AgentType>()).Returns(true);
        adapter.BuildSessionCommand(Arg.Any<AgentType>(), Arg.Any<string>(), Arg.Any<Sandbox>(), Arg.Any<string?>())
            .Returns(new AgentCommand("fake-acp", [], Path.GetTempPath(), server.Port));

        var client = new AcpSessionClient([adapter],
            new AcpSessionOptions { MaxProtocolVersion = maxProtocolVersion, HandshakeTimeout = TimeSpan.FromSeconds(5) });
        var events = new ConcurrentQueue<AgentSessionEvent>();
        client.RegisterEventListener(ThreadId, events.Enqueue);
        return (client, events, server);
    }

    private static async Task<AgentSessionEvent> WaitEventAsync(
        ConcurrentQueue<AgentSessionEvent> events, Func<AgentSessionEvent, bool> predicate, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (events.FirstOrDefault(predicate) is { } found)
            {
                return found;
            }

            await Task.Delay(20);
        }

        throw new InvalidOperationException("Timed out waiting for session event. Seen: "
            + string.Join(" | ", events.Select(e => $"{e.Kind}:{e.Content}")));
    }

    [Fact]
    public async Task Dado_MaxV2ComAgenteV2_Quando_StartSession_Entao_NegociaV2()
    {
        var (client, events, server) = Create();
        using var clientDisposable = client;
        using var serverDisposable = server;

        (await client.StartSessionAsync(ThreadId, AgentType.OpenCode, Path.GetTempPath(), Sandbox.WorkspaceWrite))
            .ShouldBeTrue(string.Join(" | ", events.Select(e => $"{e.Kind}:{e.Content}")));

        var init = await server.WaitForAsync(m =>
            m.TryGetProperty("method", out var mm) && mm.GetString() == "initialize");
        init.GetProperty("params").GetProperty("protocolVersion").GetInt32().ShouldBe(2);
        init.GetProperty("params").TryGetProperty("info", out _).ShouldBeTrue();
        init.GetProperty("params").TryGetProperty("clientCapabilities", out _).ShouldBeFalse();

        var peer = client.GetPeerInfo(ThreadId)!;
        peer.ProtocolVersion.ShouldBe(2);
        peer.SessionResume.ShouldBeTrue();
        peer.SessionClose.ShouldBeTrue();
        peer.SessionList.ShouldBeTrue();
        peer.SessionDelete.ShouldBeTrue();
        peer.McpStdio.ShouldBeTrue();
        peer.SessionId.ShouldBe("s-v2");
    }

    [Fact]
    public async Task Dado_MaxV2ComAgenteV1_Quando_StartSession_Entao_FallbackV1()
    {
        var (client, _, server) = Create(initializeResult: V1InitializeResult);
        using var clientDisposable = client;
        using var serverDisposable = server;

        (await client.StartSessionAsync(ThreadId, AgentType.OpenCode, Path.GetTempPath(), Sandbox.WorkspaceWrite))
            .ShouldBeTrue();

        client.GetPeerInfo(ThreadId)!.ProtocolVersion.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_MaxV1ComAgenteV2_Quando_StartSession_Entao_UnsupportedVersion()
    {
        var (client, events, server) = Create(maxProtocolVersion: 1, initializeResult: V2InitializeResult);
        using var clientDisposable = client;
        using var serverDisposable = server;

        (await client.StartSessionAsync(ThreadId, AgentType.OpenCode, Path.GetTempPath(), Sandbox.WorkspaceWrite))
            .ShouldBeFalse();

        var error = await WaitEventAsync(events, e => e.Kind == "error");
        error.PayloadJson!.ShouldContain("unsupported_version");
    }

    [Fact]
    public async Task Dado_SessaoV2_Quando_Prompt_Entao_AckNaoTerminaTurnoEIdleTermina()
    {
        var (client, events, server) = Create();
        using var clientDisposable = client;
        using var serverDisposable = server;
        await client.StartSessionAsync(ThreadId, AgentType.OpenCode, Path.GetTempPath(), Sandbox.WorkspaceWrite);
        while (events.TryDequeue(out _)) { } // limpa eventos do handshake

        (await client.SendPromptAsync(ThreadId, "hello")).ShouldBeTrue();

        var prompt = await server.WaitForAsync(m =>
            m.TryGetProperty("method", out var mm) && mm.GetString() == "session/prompt");
        prompt.GetProperty("params").GetProperty("prompt")[0].GetProperty("text").GetString().ShouldBe("hello");

        // O ack {messageId} NÃO encerra o turno em v2 — espera o round-trip
        // do response automático do fake e confirma que nada foi emitido.
        await Task.Delay(400);
        events.Any(e => e.Content == "Prompt turn completed").ShouldBeFalse();

        // O turno termina no state_update(idle) com stopReason.
        await server.SendRawAsync(
            """{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"s-v2","update":{"sessionUpdate":"state_update","state":"running"}}}""");
        await server.SendRawAsync(
            """{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"s-v2","update":{"sessionUpdate":"state_update","state":"idle","stopReason":"end_turn"}}}""");

        var done = await WaitEventAsync(events, e => e.Content == "Prompt turn completed");
        done.PayloadJson!.ShouldContain("end_turn");
    }

    [Fact]
    public async Task Dado_SessaoV1_Quando_PromptResponse_Entao_TurnoTerminaNoResponse()
    {
        var (client, events, server) = Create(initializeResult: V1InitializeResult);
        server.Responder = (method, _) => method switch
        {
            "initialize" => V1InitializeResult,
            "session/new" => JsonDocument.Parse("""{"sessionId":"s-v1"}""").RootElement.Clone(),
            "session/prompt" => JsonDocument.Parse("""{"stopReason":"end_turn"}""").RootElement.Clone(),
            "session/close" => JsonDocument.Parse("{}").RootElement.Clone(),
            _ => null
        };
        using var clientDisposable = client;
        using var serverDisposable = server;
        await client.StartSessionAsync(ThreadId, AgentType.OpenCode, Path.GetTempPath(), Sandbox.WorkspaceWrite);
        while (events.TryDequeue(out _)) { }

        await client.SendPromptAsync(ThreadId, "hi");

        var done = await WaitEventAsync(events, e => e.Content == "Prompt turn completed");
        done.PayloadJson!.ShouldContain("end_turn");
    }

    [Fact]
    public async Task Dado_SessaoV2_Quando_ToolCallUpdate_Entao_PrimeiroViraCreateComUpsertFields()
    {
        var (client, events, server) = Create();
        using var clientDisposable = client;
        using var serverDisposable = server;
        await client.StartSessionAsync(ThreadId, AgentType.OpenCode, Path.GetTempPath(), Sandbox.WorkspaceWrite);
        while (events.TryDequeue(out _)) { }

        await server.SendRawAsync(
            """{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"s-v2","update":{"sessionUpdate":"tool_call_update","toolCallId":"tc-1","title":"Edit","status":"in_progress"}}}""");
        await server.SendRawAsync(
            """{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"s-v2","update":{"sessionUpdate":"tool_call_update","toolCallId":"tc-1","status":"completed"}}}""");

        var created = await WaitEventAsync(events, e => e.Kind == "tool_call" && e.ToolCallId == "tc-1");
        created.PatchOp.ShouldBe("upsert");
        created.EntityKind.ShouldBe("tool_call");
        var updated = await WaitEventAsync(events, e => e.Kind == "tool_output" && e.ToolCallId == "tc-1");
        updated.PayloadJson!.ShouldContain("completed");
    }

    [Fact]
    public async Task Dado_SessaoV2_Quando_BatchComEntradaInvalida_Entao_ProcessaIndividualmente()
    {
        var (client, events, server) = Create();
        using var clientDisposable = client;
        using var serverDisposable = server;
        await client.StartSessionAsync(ThreadId, AgentType.OpenCode, Path.GetTempPath(), Sandbox.WorkspaceWrite);
        while (events.TryDequeue(out _)) { }

        await server.SendRawAsync(
            """[{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"s-v2","update":{"sessionUpdate":"state_update","state":"running"}}},{"jsonrpc":"2.0","id":"req-x","method":"elicitation/select","params":{}},42]""");

        // Notificação processada primeiro — prova que o batch foi lido.
        (await WaitEventAsync(events, e => e.Kind == "state")).ShouldNotBeNull();

        // O request desconhecido também emite evento de atividade.
        (await WaitEventAsync(events, e => e.Content?.Contains("elicitation/select") == true))
            .ShouldNotBeNull();

        // Request desconhecido → -32601 individual; entrada inválida → -32600.
        // Um único wait: WaitForAsync consome itens não-matching do inbox,
        // então duas chamadas sequenciais perderiam a primeira resposta.
        var responses = new List<JsonElement>();
        await server.WaitForAsync(m =>
        {
            if (m.TryGetProperty("error", out _))
            {
                responses.Add(m.Clone());
            }

            return responses.Count >= 2;
        });
        responses.Any(r => IsError(r, "req-x", -32601)).ShouldBeTrue();
        responses.Any(r => IsError(r, null, -32600)).ShouldBeTrue();

        static bool IsError(JsonElement r, string? expectedId, int code)
        {
            if (!r.TryGetProperty("id", out var id))
            {
                return false;
            }

            var idMatches = expectedId is null
                ? id.ValueKind == JsonValueKind.Null
                : id.GetString() == expectedId;
            return idMatches
                && r.TryGetProperty("error", out var e)
                && e.GetProperty("code").GetInt32() == code;
        }
    }

    [Fact]
    public async Task Dado_SessaoV2_Quando_FsRequest_Entao_MethodNotFound()
    {
        var (client, _, server) = Create();
        using var clientDisposable = client;
        using var serverDisposable = server;
        await client.StartSessionAsync(ThreadId, AgentType.OpenCode, Path.GetTempPath(), Sandbox.WorkspaceWrite);

        await server.SendRawAsync(
            """{"jsonrpc":"2.0","id":"fs-1","method":"fs/read_text_file","params":{"path":"/tmp/a"}}""");

        var response = await server.WaitForAsync(m =>
            m.TryGetProperty("id", out var i) && i.GetString() == "fs-1"
            && m.TryGetProperty("error", out var e) && e.GetProperty("code").GetInt32() == -32601);
        response.ShouldNotBe(default);
    }

    [Fact]
    public async Task Dado_SessaoV2_Quando_SetMode_Entao_ViraConfigOption()
    {
        var (client, _, server) = Create();
        using var clientDisposable = client;
        using var serverDisposable = server;
        await client.StartSessionAsync(ThreadId, AgentType.OpenCode, Path.GetTempPath(), Sandbox.WorkspaceWrite);

        (await client.SetModeAsync(ThreadId, "plan")).ShouldBeTrue();

        var request = await server.WaitForAsync(m =>
            m.TryGetProperty("method", out var mm) && mm.GetString() == "session/set_config_option");
        request.GetProperty("params").GetProperty("configId").GetString().ShouldBe("mode");
        request.GetProperty("params").GetProperty("value").GetString().ShouldBe("plan");
    }

    [Fact]
    public async Task Dado_SessaoV2_Quando_PermissionComTitleSubject_Entao_EventoNormalizadoEReply()
    {
        var (client, events, server) = Create();
        using var clientDisposable = client;
        using var serverDisposable = server;
        await client.StartSessionAsync(ThreadId, AgentType.OpenCode, Path.GetTempPath(), Sandbox.WorkspaceWrite);
        while (events.TryDequeue(out _)) { }

        await server.SendRawAsync(
            """{"jsonrpc":"2.0","id":"perm-1","method":"session/request_permission","params":{"sessionId":"s-v2","title":"Permitir escrita?","subject":{"type":"tool_call","toolCall":{"toolCallId":"tc-9","kind":"edit","title":"Edit"}},"options":[{"optionId":"allow_once","name":"Allow","kind":"allow_once"},{"optionId":"reject_once","name":"Reject","kind":"reject_once"}]}}""");

        var perm = await WaitEventAsync(events, e => e.Kind == "permission");
        using var payload = JsonDocument.Parse(perm.PayloadJson!);
        payload.RootElement.GetProperty("tool").GetString().ShouldBe("Permitir escrita?");

        (await client.ReplyPermissionAsync(ThreadId, "perm-1", "allow")).ShouldBeTrue();

        var reply = await server.WaitForAsync(m =>
            m.TryGetProperty("id", out var i) && i.GetString() == "perm-1"
            && m.TryGetProperty("result", out _));
        reply.GetProperty("result").GetProperty("outcome")
            .GetProperty("optionId").GetString().ShouldBe("allow_once");
    }

    /// <summary>Fake ACP agent over TCP loopback — NDJSON in/out, per-entry batch.</summary>
    private sealed class FakeAcpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Channel<JsonElement> _inbox = Channel.CreateUnbounded<JsonElement>();
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private StreamWriter? _writer;

        public Func<string, JsonElement, JsonElement?>? Responder { get; set; }
        public int Port { get; }

        public FakeAcpServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoop);
        }

        private async Task AcceptLoop()
        {
            try
            {
                var socket = await _listener.AcceptTcpClientAsync(_cts.Token);
                var stream = socket.GetStream();
                _writer = new StreamWriter(stream) { AutoFlush = true };
                var reader = new StreamReader(stream);
                while (!_cts.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(_cts.Token);
                    if (line is null)
                    {
                        break;
                    }

                    try
                    {
                        Dispatch(line);
                    }
                    catch (JsonException)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private void Dispatch(string line)
        {
            using var doc = JsonDocument.Parse(line);
            var elements = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList()
                : [doc.RootElement.Clone()];

            foreach (var el in elements)
            {
                _inbox.Writer.TryWrite(el);

                var hasId = el.TryGetProperty("id", out var idEl)
                    && idEl.ValueKind is JsonValueKind.String or JsonValueKind.Number;
                var method = el.TryGetProperty("method", out var m) ? m.GetString() : null;
                if (!hasId || method is null)
                {
                    continue;
                }

                var parameters = el.TryGetProperty("params", out var p) ? p : default;
                var result = Responder?.Invoke(method, parameters);
                if (result is { } r)
                {
                    // GetRawText preserva formatação multi-linha — serializa
                    // compacto para manter uma mensagem por linha (NDJSON).
                    _ = SendRawAsync($$"""{"jsonrpc":"2.0","id":{{idEl.GetRawText()}},"result":{{JsonSerializer.Serialize(r)}}}""");
                }
            }
        }

        public async Task SendRawAsync(string json)
        {
            await _writeLock.WaitAsync();
            try
            {
                if (_writer is not null)
                {
                    await _writer.WriteLineAsync(json);
                    await _writer.FlushAsync();
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task<JsonElement> WaitForAsync(
            Func<JsonElement, bool> predicate, int timeoutMs = 5000)
        {
            var seen = new List<string>();
            try
            {
                using var cts = new CancellationTokenSource(timeoutMs);
                while (true)
                {
                    var el = await _inbox.Reader.ReadAsync(cts.Token);
                    seen.Add(el.GetRawText());
                    if (predicate(el))
                    {
                        return el;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException(
                    "WaitForAsync timeout. Seen: " + string.Join(" | ", seen));
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _cts.Dispose();
        }
    }
}
