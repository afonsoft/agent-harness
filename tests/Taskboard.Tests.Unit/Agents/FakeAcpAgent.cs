using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Taskboard.Agents;
using Taskboard.ValueObjects;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// Loopback TCP ACP agent for conformance tests (RF-014 transport): speaks
/// NDJSON JSON-RPC, records every inbound message and answers requests via a
/// scripted <see cref="Responder"/>.
/// </summary>
internal sealed class FakeAcpAgent : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly ConcurrentQueue<JsonDocument> _received = new();
    private readonly CancellationTokenSource _cts = new();
    private StreamWriter? _writer;
    private Task? _loop;

    public FakeAcpAgent()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
    }

    public int Port { get; private set; }

    /// <summary>Request handler: (id, method, params) → result JSON text, or null to not answer.</summary>
    public Func<string, string, JsonElement, string?>? Responder { get; set; }

    /// <summary>All inbound lines (requests, notifications, our responses to agent requests).</summary>
    public IReadOnlyList<JsonDocument> Received => _received.ToArray();

    /// <summary>Adapter that routes the session client to this TCP agent.</summary>
    public IAgentAdapter Adapter => new TcpAdapter(this);

    public Task StartAsync()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = Task.Run(AcceptLoopAsync);
        return Task.CompletedTask;
    }

    /// <summary>Writes a raw NDJSON line to the client (notification or response).</summary>
    public async Task SendLineAsync(string json)
    {
        if (_writer is null)
        {
            throw new InvalidOperationException("Client not connected yet.");
        }

        await _writer.WriteLineAsync(json);
        await _writer.FlushAsync();
    }

    /// <summary>Waits until an inbound line matches <paramref name="match"/>.</summary>
    public async Task<JsonElement> WaitForAsync(
        Func<JsonElement, bool> match, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(15));
        while (!cts.IsCancellationRequested)
        {
            foreach (var doc in _received)
            {
                if (match(doc.RootElement))
                {
                    return doc.RootElement.Clone();
                }
            }

            await Task.Delay(20, cts.Token).ContinueWith(_ => { });
        }

        throw new TimeoutException("FakeAcpAgent: expected inbound message never arrived.");
    }

    /// <summary>True when a line matching <paramref name="match"/> was received.</summary>
    public bool ReceivedMessage(Func<JsonElement, bool> match) =>
        _received.Any(d => match(d.RootElement));

    private async Task AcceptLoopAsync()
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

                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(line);
                }
                catch (JsonException)
                {
                    continue;
                }

                _received.Enqueue(doc);
                await MaybeRespondAsync(doc.RootElement);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
    }

    private async Task MaybeRespondAsync(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("id", out var idEl)
            || !root.TryGetProperty("method", out var methodEl))
        {
            return; // notification or a response to an agent request
        }

        var id = idEl.ValueKind == JsonValueKind.String ? idEl.GetString()! : idEl.GetRawText();
        var method = methodEl.GetString() ?? string.Empty;
        var p = root.TryGetProperty("params", out var pe) ? pe : default;

        var result = Responder?.Invoke(id, method, p);
        if (result is not null && _writer is not null)
        {
            // NDJSON: scripted results may be multi-line raw literals — JSON
            // whitespace is insignificant, so collapse newlines to one line.
            var inline = result.Replace('\n', ' ').Replace('\r', ' ');
            await _writer.WriteLineAsync($"{{\"jsonrpc\":\"2.0\",\"id\":{JsonSerializer.Serialize(id)},\"result\":{inline}}}");
            await _writer.FlushAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        try
        {
            if (_loop is not null)
            {
                await _loop;
            }
        }
        catch
        {
            // best-effort
        }

        _listener.Stop();
        _cts.Dispose();
        foreach (var doc in _received)
        {
            doc.Dispose();
        }
    }

    private sealed class TcpAdapter(FakeAcpAgent agent) : IAgentAdapter
    {
        public bool CanHandle(AgentType agentType) => true;

        public AgentCommand BuildCommand(AgentExecutionRequest request) =>
            new("unused", [], "/tmp", agent.Port);

        public bool SupportsInteractiveSession => true;

        public AgentCommand BuildSessionCommand(
            AgentType agentType, string workdir, Sandbox sandbox, string? modelName = null) =>
            new("unused", [], workdir, agent.Port);
    }
}
