using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Integrations.Chat.Lsp;
using Taskboard.Integrations.Chat.Tools;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>SPEC-20261025 C4 — LSP framing, session handshake, tool surface.</summary>
public sealed class ChatLspTests
{
    [Fact]
    public async Task Dado_Frame_Quando_WriteDepoisRead_Entao_Roundtrip()
    {
        var stream = new MemoryStream();
        await LspProtocol.WriteMessageAsync(stream, new { jsonrpc = "2.0", id = 7, method = "ping" });
        stream.Position = 0;

        using var doc = await LspProtocol.ReadMessageAsync(stream);
        doc.ShouldNotBeNull();
        doc.RootElement.GetProperty("id").GetInt32().ShouldBe(7);
        doc.RootElement.GetProperty("method").GetString().ShouldBe("ping");
    }

    [Fact]
    public async Task Dado_HeadersExtras_Quando_Read_Entao_ContentLengthVence()
    {
        var body = "{\"jsonrpc\":\"2.0\"}"u8.ToArray();
        var stream = new MemoryStream();
        var writer = new StreamWriter(stream);
        await writer.WriteAsync(
            $"Content-Type: application/vscode-jsonrpc; charset=utf-8\r\nContent-Length: {body.Length}\r\n\r\n");
        await writer.FlushAsync();
        await stream.WriteAsync(body);
        stream.Position = 0;

        using var doc = await LspProtocol.ReadMessageAsync(stream);
        doc.ShouldNotBeNull();
        doc.RootElement.GetProperty("jsonrpc").GetString().ShouldBe("2.0");
    }

    [Fact]
    public async Task Dado_ServidorFake_Quando_InitializeEDocumentSymbol_Entao_ResultadoCorrelacionado()
    {
        var (client, stop) = StartClientWithFakeServer();
        try
        {
            await client.InitializeAsync("file:///tmp/ws", CancellationToken.None);
            var result = await client.RequestAsync(
                "textDocument/documentSymbol",
                new { textDocument = new { uri = "file:///tmp/ws/a.cs" } },
                CancellationToken.None);

            result.ShouldNotBeNull();
            var arr = result.Value;
            arr.ValueKind.ShouldBe(JsonValueKind.Array);
            arr[0].GetProperty("name").GetString().ShouldBe("Root");
            arr[0].GetProperty("children")[0].GetProperty("name").GetString().ShouldBe("Child");
        }
        finally
        {
            client.Dispose();
            await stop;
        }
    }

    [Fact]
    public async Task Dado_RespostaErro_Quando_Request_Entao_Excecao()
    {
        var (client, stop) = StartClientWithFakeServer(failOn: "bad/method");
        try
        {
            await Should.ThrowAsync<InvalidOperationException>(
                () => client.RequestAsync("bad/method", null, CancellationToken.None));
        }
        finally
        {
            client.Dispose();
            await stop;
        }
    }

    [Fact]
    public async Task Dado_DocumentSymbols_Quando_Execute_Entao_SimbolosAchatados()
    {
        var pool = new FakePool(StubClient.Returning("""
            [{"name":"Root","kind":5,"selectionRange":{"start":{"line":1,"character":0}},
              "children":[{"name":"Run","kind":6,"selectionRange":{"start":{"line":3,"character":8}}}]}]
            """));
        var tool = new LspTool(pool);
        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"action":"document_symbols","path":"src/A.cs"}""").RootElement,
            Context(), CancellationToken.None);

        var doc = JsonDocument.Parse(result.Json);
        var results = doc.RootElement.GetProperty("results");
        results.GetArrayLength().ShouldBe(2);
        results[1].GetProperty("name").GetString().ShouldBe("Run");
        results[1].GetProperty("container").GetString().ShouldBe("Root");
        results[1].GetProperty("line").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task Dado_GotoDefinition_Quando_Execute_Entao_LocationNormalizada()
    {
        var pool = new FakePool(StubClient.Returning("""
            [{"uri":"file:///ws/B.cs","range":{"start":{"line":9,"character":4}}}]
            """));
        var tool = new LspTool(pool);
        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"action":"goto_definition","path":"src/A.cs","line":2,"character":10}""").RootElement,
            Context(), CancellationToken.None);

        var doc = JsonDocument.Parse(result.Json);
        var item = doc.RootElement.GetProperty("results")[0];
        item.GetProperty("file").GetString().ShouldBe("file:///ws/B.cs");
        item.GetProperty("line").GetInt32().ShouldBe(9);
        // The client received the right method+params.
        ((StubClient)pool.LastClient!).LastMethod.ShouldBe("textDocument/definition");
    }

    [Fact]
    public async Task Dado_PathEscape_Quando_Execute_Entao_Recusado()
    {
        var tool = new LspTool(new FakePool(StubClient.Returning("[]")));
        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"action":"document_symbols","path":"../../etc/passwd"}""").RootElement,
            Context(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_PoolIndisponivel_Quando_Execute_Entao_Recusado()
    {
        var tool = new LspTool(new FakePool(null));
        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"action":"document_symbols","path":"src/A.cs"}""").RootElement,
            Context(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.Json.ShouldContain("unavailable");
    }

    [Fact]
    public void Dado_Defaults_Quando_Catalogo_Entao_LspDeferido()
    {
        Taskboard.Application.Chat.DeferredToolCatalog.DefaultDeferred.ShouldContain("lsp");
    }

    [Fact]
    public void Dado_ServerConfigurado_Quando_IsServerAvailable_Entao_FalsoSemBinario()
    {
        var pool = new LspClientPool(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [LspClientPool.ServerKey] = "definitely-not-a-real-lsp-bin-xyz",
            }).Build());
        pool.IsServerAvailable().ShouldBeFalse();
    }

    private static ChatToolContext Context() =>
        new(WorkspacePath: "/tmp", ProviderId: Guid.NewGuid(), ProviderBaseUrl: "http://x",
            ProviderApiKey: "k", ImageModel: "", SearchBackend: "none",
            SearchUrl: "", SearchApiKey: "");

    /// <summary>Two anonymous pipe pairs + a loopback server task answering per method.</summary>
    private static (LspClient Client, Task Stop) StartClientWithFakeServer(string? failOn = null)
    {
        var serverRead = new AnonymousPipeServerStream(PipeDirection.In);
        var clientWrite = new AnonymousPipeClientStream(PipeDirection.Out, serverRead.ClientSafePipeHandle);
        var serverWrite = new AnonymousPipeServerStream(PipeDirection.Out);
        var clientRead = new AnonymousPipeClientStream(PipeDirection.In, serverWrite.ClientSafePipeHandle);

        var stop = Task.Run(async () =>
        {
            try
            {
                while (await LspProtocol.ReadMessageAsync(serverRead, CancellationToken.None) is { } doc)
                {
                    using (doc)
                    {
                        var root = doc.RootElement;
                        if (!root.TryGetProperty("id", out var idEl))
                        {
                            continue; // notifications (initialized) — no response
                        }

                        var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
                        if (method == failOn)
                        {
                            await LspProtocol.WriteMessageAsync(serverWrite, new
                            {
                                jsonrpc = "2.0",
                                id = idEl.GetInt64(),
                                error = new { code = -32601, message = "boom" },
                            });
                            continue;
                        }

                        object? result = method switch
                        {
                            "initialize" => new { capabilities = new { } },
                            "textDocument/documentSymbol" => (object)new object[]
                            {
                                new
                                {
                                    name = "Root", kind = 5,
                                    selectionRange = new { start = new { line = 0, character = 0 } },
                                    children = new[]
                                    {
                                        new { name = "Child", kind = 6, selectionRange = new { start = new { line = 1, character = 2 } } },
                                    },
                                },
                            },
                            _ => null,
                        };
                        await LspProtocol.WriteMessageAsync(serverWrite, new
                        {
                            jsonrpc = "2.0",
                            id = idEl.GetInt64(),
                            result,
                        });
                    }
                }
            }
            catch
            {
                // pipes closed on client dispose — loop exits
            }
            finally
            {
                serverRead.Dispose();
                serverWrite.Dispose();
            }
        });

        return (new LspClient(clientRead, clientWrite), stop);
    }

    private sealed class StubClient(string resultJson) : ILspClient
    {
        public string? LastMethod { get; private set; }

        public static StubClient Returning(string resultJson) => new(resultJson);

        public Task InitializeAsync(string rootUri, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<JsonElement?> RequestAsync(string method, object? parameters, CancellationToken cancellationToken)
        {
            LastMethod = method;
            return Task.FromResult<JsonElement?>(JsonDocument.Parse(resultJson).RootElement.Clone());
        }
    }

    private sealed class FakePool(ILspClient? client) : ILspClientPool
    {
        public ILspClient? LastClient => client;

        public bool IsServerAvailable() => client is not null;

        public Task<ILspClient?> GetAsync(string workspacePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(client);
    }
}
