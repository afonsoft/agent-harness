using System.Collections.Concurrent;
using System.Text.Json;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Integrations.Chat.Lsp;

/// <summary>
/// SPEC-20261025 C4: one JSON-RPC session over two streams — the server's
/// stdout (we read) and stdin (we write). Responses complete pending
/// requests by <c>id</c>; notifications and log noise are drained and
/// dropped. A dead stream faults every waiter so the caller reaps the
/// session instead of hanging.
/// </summary>
internal sealed class LspClient : ILspClient, IDisposable
{
    private readonly Stream _read;
    private readonly Stream _write;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly Task _readLoop;
    private int _nextId;

    /// <summary>Stream ctor — tests drive both ends in-memory.</summary>
    public LspClient(Stream readFrom, Stream writeTo)
    {
        _read = readFrom;
        _write = writeTo;
        _readLoop = Task.Run(ReadLoopAsync);
    }

    public async Task InitializeAsync(string rootUri, CancellationToken cancellationToken)
    {
        await RequestAsync("initialize", new
        {
            processId = Environment.ProcessId,
            rootUri,
            workspaceFolders = new[] { new { uri = rootUri, name = "workspace" } },
            capabilities = new
            {
                textDocument = new
                {
                    documentSymbol = new { hierarchicalDocumentSymbolSupport = true },
                    definition = new { },
                    references = new { },
                    diagnostic = new { },
                },
                workspace = new { symbol = new { } },
            },
        }, cancellationToken).ConfigureAwait(false);
        await NotifyAsync("initialized", new { }).ConfigureAwait(false);
    }

    public async Task<JsonElement?> RequestAsync(
        string method, object? parameters, CancellationToken cancellationToken)
    {
        var id = (long)Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        try
        {
            await WriteAsync(new { jsonrpc = "2.0", id, method, @params = parameters }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }

        await using var reg = cancellationToken.Register(
            () => tcs.TrySetCanceled(cancellationToken)).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    private Task NotifyAsync(string method, object? parameters) =>
        WriteAsync(new { jsonrpc = "2.0", method, @params = parameters }, CancellationToken.None);

    private async Task WriteAsync(object message, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await LspProtocol.WriteMessageAsync(_write, message, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (true)
            {
                using var doc = await LspProtocol.ReadMessageAsync(_read, CancellationToken.None)
                    .ConfigureAwait(false);
                if (doc is null)
                {
                    break;
                }

                var root = doc.RootElement;
                if (!root.TryGetProperty("id", out var idEl))
                {
                    continue; // notification — drained, dropped
                }

                var id = idEl.ValueKind == JsonValueKind.Number
                    ? idEl.GetInt64()
                    : long.TryParse(idEl.GetString(), out var n) ? n : -1;
                if (!_pending.TryRemove(id, out var tcs))
                {
                    continue;
                }

                if (root.TryGetProperty("error", out var error))
                {
                    tcs.TrySetException(new InvalidOperationException(
                        $"lsp: request {id} failed — {error.GetRawText()}"));
                }
                else if (root.TryGetProperty("result", out var result))
                {
                    tcs.TrySetResult(result.Clone());
                }
                else
                {
                    tcs.TrySetException(new InvalidOperationException(
                        $"lsp: request {id} returned neither result nor error"));
                }
                // notifications (publishDiagnostics, window/logMessage) — drained, dropped.
            }
        }
        catch
        {
            // fall through — fault all waiters below
        }

        var gone = new EndOfStreamException("lsp: server stream closed");
        foreach (var (id, tcs) in _pending)
        {
            if (_pending.TryRemove(id, out _))
            {
                tcs.TrySetException(gone);
            }
        }
    }

    public void Dispose()
    {
        try { _write.Close(); } catch { /* best effort */ }
        try { _read.Close(); } catch { /* best effort */ }
    }
}
