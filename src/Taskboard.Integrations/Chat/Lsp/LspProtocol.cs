using System.Text;
using System.Text.Json;

namespace Taskboard.Integrations.Chat.Lsp;

/// <summary>
/// SPEC-20261025 C4: LSP wire framing — <c>Content-Length</c> headers +
/// JSON body, per the base protocol. Read returns the parsed document;
/// the caller owns its lifetime (<see cref="JsonElement.Clone"/> anything
/// it keeps).
/// </summary>
internal static class LspProtocol
{
    /// <summary>Cap on a single frame — a rogue Content-Length never OOMs the loop.</summary>
    internal const int MaxFrameBytes = 4 * 1024 * 1024;

    public static async Task WriteMessageAsync(
        Stream stream, object message, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one frame; null on EOF/stream close.</summary>
    public static async Task<JsonDocument?> ReadMessageAsync(
        Stream stream, CancellationToken cancellationToken = default)
    {
        var contentLength = await ReadContentLengthAsync(stream, cancellationToken).ConfigureAwait(false);
        if (contentLength is null)
        {
            return null;
        }
        if (contentLength is <= 0 or > MaxFrameBytes)
        {
            throw new InvalidDataException($"lsp: invalid Content-Length {contentLength}");
        }

        var body = new byte[contentLength.Value];
        await ReadExactAsync(stream, body, cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(body);
    }

    /// <summary>Header lines until the blank separator; null on EOF before any byte.</summary>
    private static async Task<int?> ReadContentLengthAsync(Stream stream, CancellationToken ct)
    {
        int? length = null;
        var sawAny = false;
        var line = new StringBuilder();

        while (true)
        {
            var b = new byte[1];
            var read = await stream.ReadAsync(b, ct).ConfigureAwait(false);
            if (read == 0)
            {
                return sawAny ? throw new EndOfStreamException("lsp: EOF inside headers") : null;
            }
            sawAny = true;

            var ch = (char)b[0];
            if (ch is '\r')
            {
                continue;
            }
            if (ch is '\n')
            {
                if (line.Length == 0)
                {
                    return length; // blank line ends the header block
                }

                var s = line.ToString();
                if (s.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(s["Content-Length:".Length..].Trim(), out var n))
                {
                    length = n;
                }
                line.Clear();
                continue;
            }
            line.Append(ch);
        }
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), ct).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("lsp: EOF inside body");
            }
            offset += read;
        }
    }
}
