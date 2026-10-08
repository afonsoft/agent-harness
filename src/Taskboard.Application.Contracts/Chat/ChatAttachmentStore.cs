using System.Security.Cryptography;
using System.Text;

namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// Byte store for composer attachments (SPEC-20261005-chat-attachments-
/// feedback RNF-004). Files live under <c>&lt;dataDir&gt;/attachments</c> as
/// <c>{attachmentId}.{ext}</c> — the row id is the address, so
/// <c>attach://{id}</c> resolves without a DB lookup. Writes are
/// append-only (forked copies share the path — open question #2).
/// </summary>
public sealed class ChatAttachmentStore(string dataDir)
{
    public string Root => Path.Join(dataDir, "attachments");

    /// <summary>Extension (with dot) for a sniffed MIME, or null when unknown.</summary>
    public static string? ExtensionFor(string contentType) => contentType switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/svg+xml" => ".svg",
        "application/pdf" => ".pdf",
        "text/csv" => ".csv",
        "application/json" => ".json",
        "text/markdown" => ".md",
        "text/html" => ".html",
        "text/xml" or "application/xml" => ".xml",
        _ when contentType.StartsWith("text/", StringComparison.Ordinal) => ".txt",
        _ => null,
    };

    /// <summary>Persists bytes; returns the storage-relative path + sha256 hex.</summary>
    public (string StoragePath, string Sha256) Save(
        string attachmentId, string contentType, byte[] bytes)
    {
        var ext = ExtensionFor(contentType) ?? ".bin";
        var relative = $"{attachmentId}{ext}";
        Directory.CreateDirectory(Root);
        File.WriteAllBytes(Path.Join(Root, relative), bytes);
        return (relative, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    /// <summary>Resolves <c>attach://{id}</c> to (fullPath, MIME) or null.</summary>
    public (string FullPath, string ContentType)? Resolve(string attachmentId)
    {
        if (string.IsNullOrWhiteSpace(attachmentId)
            || attachmentId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || attachmentId.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        if (!Directory.Exists(Root))
        {
            return null;
        }

        var file = Directory.EnumerateFiles(Root, $"{attachmentId}.*").FirstOrDefault();
        return file is null ? null : (file, MimeForExtension(Path.GetExtension(file)));
    }

    public byte[]? ReadBytes(string storagePath)
    {
        var full = ResolvePath(storagePath);
        return full is null ? null : File.ReadAllBytes(full);
    }

    /// <summary>Full path for a storage-relative name; null on traversal/missing.</summary>
    public string? ResolvePath(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath)
            || storagePath.Contains('/', StringComparison.Ordinal)
            || storagePath.Contains('\\', StringComparison.Ordinal)
            || storagePath.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        var full = Path.Join(Root, storagePath);
        return File.Exists(full) ? full : null;
    }

    /// <summary>Deletes a bound/staged file; best-effort.</summary>
    public void Delete(string storagePath)
    {
        var full = ResolvePath(storagePath);
        if (full is not null)
        {
            File.Delete(full);
        }
    }

    private static string MimeForExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".pdf" => "application/pdf",
        ".csv" => "text/csv",
        ".json" => "application/json",
        ".md" => "text/markdown",
        ".html" => "text/html",
        ".xml" => "text/xml",
        _ => "application/octet-stream",
    };
}

/// <summary>
/// Sniffs MIME from leading bytes (SPEC-20261005 RF-008/RNF-002) — the
/// declared <c>Content-Type</c> header is never trusted. Images/pdf by magic
/// bytes; everything else is accepted only when it decodes as clean UTF-8
/// text and is tagged by the declared extension when it is a known text
/// subtype.
/// </summary>
public static class ChatAttachmentSniffer
{
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PdfMagic = [0x25, 0x50, 0x44, 0x46];
    private static readonly byte[] RiffMagic = [0x52, 0x49, 0x46, 0x46];

    /// <summary>
    /// Default allowlist (SPEC-20261005 RF-008) — images, texts, pdf and the
    /// common structured text subtypes.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultAllowedMime =
        ["image/*", "text/*", "application/pdf", "application/json"];

    /// <summary>MIME allowlist match — exact entry or <c>prefix/*</c> wildcard.</summary>
    public static bool IsAllowed(string contentType, IReadOnlyList<string> allowed)
    {
        foreach (var entry in allowed)
        {
            if (entry.EndsWith("/*", StringComparison.Ordinal)
                && contentType.StartsWith(entry[..^1], StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(entry, contentType, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns the sniffed MIME when allowlisted, else null.</summary>
    public static string? Sniff(ReadOnlySpan<byte> head, string? declaredFileName, IReadOnlyList<string> allowed)
    {
        string? sniffed = null;
        if (head.Length >= 4 && head[..4].SequenceEqual(PngMagic))
        {
            sniffed = "image/png";
        }
        else if (head.Length >= 3 && head[..3].SequenceEqual(JpegMagic))
        {
            sniffed = "image/jpeg";
        }
        else if (head.Length >= 6 && (head[..6].SequenceEqual("GIF87a"u8) || head[..6].SequenceEqual("GIF89a"u8)))
        {
            sniffed = "image/gif";
        }
        else if (head.Length >= 12 && head[..4].SequenceEqual(RiffMagic)
            && head[8..12].SequenceEqual("WEBP"u8))
        {
            sniffed = "image/webp";
        }
        else if (head.Length >= 4 && head[..4].SequenceEqual(PdfMagic))
        {
            sniffed = "application/pdf";
        }
        else if (LooksLikeSvg(head))
        {
            sniffed = "image/svg+xml";
        }
        else if (LooksLikeText(head))
        {
            sniffed = TextMimeFor(declaredFileName);
        }

        return sniffed is not null && IsAllowed(sniffed, allowed) ? sniffed : null;
    }

    private static bool LooksLikeSvg(ReadOnlySpan<byte> head)
    {
        var text = Encoding.UTF8.GetString(head).TrimStart();
        return text.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
            || (text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
                && text.Contains("<svg", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>UTF-8 decodable, no NUL — a text/* payload (RNF-002).</summary>
    private static bool LooksLikeText(ReadOnlySpan<byte> head)
    {
        if (head.IsEmpty)
        {
            return true; // empty files are valid text
        }

        if (head.IndexOf((byte)0) >= 0)
        {
            return false;
        }

        try
        {
            // Strict decode — throws on malformed sequences.
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetCharCount(head);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static string TextMimeFor(string? declaredFileName) =>
        Path.GetExtension(declaredFileName ?? string.Empty).ToLowerInvariant() switch
        {
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".md" or ".markdown" => "text/markdown",
            ".html" or ".htm" => "text/html",
            ".xml" => "text/xml",
            _ => "text/plain",
        };
}

/// <summary>
/// Per-run tracker of file mutations performed by file tools
/// (SPEC-20261005-chat-attachments-feedback RF-007). The executor drains the
/// collected edits at run end into <c>ChatRunDeliverable</c> rows.
/// </summary>
public interface IChatFileEditTracker
{
    /// <summary>Captures the file's pre-edit content (or null when absent).</summary>
    void BeforeEdit(string runId, string conversationId, string path, string? beforeContent);

    /// <summary>Records the post-edit content once the write succeeds.</summary>
    void AfterEdit(string runId, string conversationId, string path, string? afterContent);

    /// <summary>Records a model-declared deliverable (<c>present</c> tool).</summary>
    void Declare(string runId, string conversationId, string path, string? summary);

    /// <summary>Returns the tracked edits for the run and drops them.</summary>
    IReadOnlyList<ChatFileEdit> Drain(string runId);
}

/// <summary>One tracked file mutation/declaration.</summary>
public sealed record ChatFileEdit(
    string Path, string? BeforeContent, string? AfterContent, string Source, string? Summary);

/// <summary>Default in-memory tracker — bounded per run.</summary>
public sealed class ChatFileEditTracker : IChatFileEditTracker
{
    private const int MaxPerRun = 200;
    private readonly Dictionary<string, List<ChatFileEdit>> _edits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ChatFileEdit> _pendingBefore = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public void BeforeEdit(string runId, string conversationId, string path, string? beforeContent)
    {
        lock (_gate)
        {
            _pendingBefore[$"{runId}\n{conversationId}\n{path}"] =
                new ChatFileEdit(path, beforeContent, null, "tool-edit", null);
        }
    }

    public void AfterEdit(string runId, string conversationId, string path, string? afterContent)
    {
        lock (_gate)
        {
            var key = $"{runId}\n{conversationId}\n{path}";
            _pendingBefore.TryGetValue(key, out var pending);
            _pendingBefore.Remove(key);
            var list = Edits(runId, conversationId);
            if (list.Count < MaxPerRun)
            {
                // Coalesce repeat edits on the same path — keep the FIRST
                // before-state and the LAST after-state.
                var existing = list.FindIndex(e => e.Path == path && e.Source == "tool-edit");
                if (existing >= 0)
                {
                    list[existing] = list[existing] with { AfterContent = afterContent };
                }
                else
                {
                    list.Add(new ChatFileEdit(path, pending?.BeforeContent, afterContent, "tool-edit", null));
                }
            }
        }
    }

    public void Declare(string runId, string conversationId, string path, string? summary)
    {
        lock (_gate)
        {
            var list = Edits(runId, conversationId);
            if (list.Count < MaxPerRun)
            {
                list.Add(new ChatFileEdit(path, null, null, "present", summary));
            }
        }
    }

    public IReadOnlyList<ChatFileEdit> Drain(string runId)
    {
        lock (_gate)
        {
            var prefix = $"{runId}\n";
            var result = _edits
                .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                .SelectMany(kv => kv.Value)
                .ToList();
            foreach (var key in _edits.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                _edits.Remove(key);
            }

            foreach (var key in _pendingBefore.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                _pendingBefore.Remove(key);
            }

            return result;
        }
    }

    private List<ChatFileEdit> Edits(string runId, string conversationId)
    {
        var key = $"{runId}\n{conversationId}";
        if (!_edits.TryGetValue(key, out var list))
        {
            list = [];
            _edits[key] = list;
        }

        return list;
    }
}
