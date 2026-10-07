using System.Text.Json;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Harness;
using Taskboard.Integrations.Harness.Security;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>Reads a file inside the workspace — path-jailed (RF-006).</summary>
public sealed class ReadFileTool(
    ISecretRedactor redactor, ISpillStore? spillStore = null,
    ChatAttachmentStore? attachmentStore = null) : IChatTool
{
    public string Name => "read_file";
    public string Description =>
        "Read a text file inside the workspace (path-jailed). Large files can be paged "
        + "with offset/limit (characters) instead of reading the whole file at once. "
        + "A path of the form spill://{id} reads a spilled tool output persisted by "
        + "context compaction (paged with offset/limit too); attach://{id} reads a "
        + "composer attachment — text types return content, pdf returns a descriptor, "
        + "images must go through read_image instead.";
    public string ParametersJson => """
        {"type":"object","properties":{"path":{"type":"string","description":"Relative path inside the workspace, spill://{id} for a spilled tool output, or attach://{id} for a composer attachment"},"offset":{"type":"integer","description":"Character offset to start reading at (default 0)"},"limit":{"type":"integer","description":"Max characters to return (default 16000)"}},"required":["path"]}
        """;

    private const string SpillScheme = "spill://";
    private const string AttachScheme = "attach://";

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var path = arguments.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? string.Empty
            : string.Empty;
        var offset = arguments.TryGetProperty("offset", out var o) && o.TryGetInt32(out var ov) && ov > 0 ? ov : 0;
        var limit = arguments.TryGetProperty("limit", out var l) && l.TryGetInt32(out var lv) && lv > 0
            ? Math.Min(lv, ShellExecTool.MaxOutputChars)
            : ShellExecTool.MaxOutputChars;
        // SPEC-20261005-chat-context-management RF-006: spill:// pointers page
        // the persisted tool output without touching the workspace jail.
        if (path.StartsWith(SpillScheme, StringComparison.OrdinalIgnoreCase))
        {
            var spillId = path[SpillScheme.Length..];
            var spill = spillStore?.Read(spillId, offset, limit);
            return spill is null
                ? new ChatToolResult(JsonSerializer.Serialize(
                    new { error = $"spill '{spillId}' not found or expired" }), Refused: false, $"spill '{spillId}' not found")
                : new ChatToolResult(JsonSerializer.Serialize(new
                {
                    path,
                    totalChars = spill.Value.TotalChars,
                    offset,
                    truncated = offset + spill.Value.Content.Length < spill.Value.TotalChars,
                    content = redactor.Redact(spill.Value.Content) ?? string.Empty,
                }));
        }

        // SPEC-20261005-chat-attachments-feedback RF-003: attach:// resolves
        // by row id ({id}.{ext} glob — no DB lookup); text types page like a
        // file, pdf returns a raw descriptor (P1), images defer to read_image.
        if (path.StartsWith(AttachScheme, StringComparison.OrdinalIgnoreCase))
        {
            var attachId = path[AttachScheme.Length..];
            var resolved = attachmentStore?.Resolve(attachId);
            if (resolved is null)
            {
                return new ChatToolResult(JsonSerializer.Serialize(
                    new { error = $"attachment '{attachId}' not found" }), RefusalReason: $"attachment '{attachId}' not found");
            }

            var (fullPath, mime) = resolved.Value;
            if (mime.StartsWith("image/", StringComparison.Ordinal))
            {
                return new ChatToolResult(JsonSerializer.Serialize(new
                {
                    path,
                    contentType = mime,
                    note = "image attachment — call read_image with the same attach:// path to see it",
                }));
            }

            if (mime == "application/pdf")
            {
                // P1: no text extraction — the descriptor is the contract.
                return new ChatToolResult(JsonSerializer.Serialize(new
                {
                    path,
                    contentType = mime,
                    bytes = new FileInfo(fullPath).Length,
                    note = "pdf attachment — raw descriptor only in this version",
                }));
            }

            var attachContent = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
            var attachTotal = attachContent.Length;
            if (offset > 0)
            {
                attachContent = offset < attachContent.Length ? attachContent[offset..] : string.Empty;
            }

            var attachTruncated = attachContent.Length > limit;
            if (attachTruncated)
            {
                attachContent = attachContent[..limit];
            }

            return new ChatToolResult(JsonSerializer.Serialize(new
            {
                path,
                contentType = mime,
                totalChars = attachTotal,
                offset,
                truncated = attachTruncated,
                content = redactor.Redact(attachContent) ?? string.Empty,
            }));
        }

        try
        {
            var full = PathJailValidator.Validate(path, context.WorkspacePath);
            var content = await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false);
            var total = content.Length;
            if (offset > 0)
            {
                content = offset < content.Length ? content[offset..] : string.Empty;
            }

            var truncated = content.Length > limit;
            if (truncated)
            {
                content = content[..limit];
            }

            return new ChatToolResult(JsonSerializer.Serialize(new
            {
                path,
                totalChars = total,
                offset,
                truncated,
                content = redactor.Redact(content) ?? string.Empty,
            }));
        }
        catch (Exception ex) when (ex is SecurityAccessDeniedException or IOException or UnauthorizedAccessException)
        {
            return new ChatToolResult(
                JsonSerializer.Serialize(new { error = ex.Message }),
                Refused: ex is SecurityAccessDeniedException,
                ex.Message);
        }
    }
}

/// <summary>Writes a file inside the workspace — path-jailed (RF-006).</summary>
public sealed class WriteFileTool(IChatFileEditTracker? editTracker = null) : IChatTool
{
    public string Name => "write_file";
    public string Description =>
        "Create or overwrite a text file on disk now inside the workspace (path-jailed) — "
        + "the file is actually written when the tool runs. Use it to save real files, not "
        + "to show the content for the user to paste.";
    public string ParametersJson => """
        {"type":"object","properties":{"path":{"type":"string","description":"Relative path inside the workspace"},"content":{"type":"string","description":"Full file content"}},"required":["path","content"]}
        """;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var path = arguments.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? string.Empty
            : string.Empty;
        var content = arguments.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString() ?? string.Empty
            : string.Empty;
        try
        {
            var full = PathJailValidator.Validate(path, context.WorkspacePath);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // RF-007: capture the pre-write content for the deliverables card.
            TrackBefore(context, path, full);
            await File.WriteAllTextAsync(full, content, cancellationToken).ConfigureAwait(false);
            TrackAfter(context, path, content);
            return new ChatToolResult(JsonSerializer.Serialize(new { path, bytes = content.Length, written = true }));
        }
        catch (Exception ex) when (ex is SecurityAccessDeniedException or IOException or UnauthorizedAccessException)
        {
            return new ChatToolResult(
                JsonSerializer.Serialize(new { error = ex.Message }),
                Refused: ex is SecurityAccessDeniedException,
                ex.Message);
        }
    }

    private void TrackBefore(ChatToolContext context, string path, string full)
    {
        if (editTracker is null || context.RunId is null || context.ConversationId is null)
        {
            return;
        }

        editTracker.BeforeEdit(
            context.RunId, context.ConversationId, path,
            File.Exists(full) ? File.ReadAllText(full) : null);
    }

    private void TrackAfter(ChatToolContext context, string path, string after)
    {
        if (editTracker is null || context.RunId is null || context.ConversationId is null)
        {
            return;
        }

        editTracker.AfterEdit(context.RunId, context.ConversationId, path, after);
    }
}

/// <summary>Lists a workspace directory — path-jailed (RF-006).</summary>
public sealed class ListDirTool() : IChatTool
{
    public string Name => "list_dir";
    public string Description => "List files and directories inside the workspace (path-jailed).";
    public string ParametersJson => """
        {"type":"object","properties":{"path":{"type":"string","description":"Relative directory path; empty for the workspace root"}}}
        """;

    public Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var path = arguments.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? string.Empty
            : string.Empty;
        try
        {
            var full = PathJailValidator.Validate(string.IsNullOrWhiteSpace(path) ? "." : path, context.WorkspacePath);
            var entries = Directory
                .EnumerateFileSystemEntries(full)
                .Select(e => $"{(Directory.Exists(e) ? "d" : "-")} {Path.GetFileName(e)}")
                .OrderBy(e => e, StringComparer.Ordinal)
                .Take(500)
                .ToList();
            return Task.FromResult(new ChatToolResult(JsonSerializer.Serialize(new
            {
                path,
                entries = entriesOrEmpty(entries),
            })));
        }
        catch (Exception ex) when (ex is SecurityAccessDeniedException or IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(new ChatToolResult(
                JsonSerializer.Serialize(new { error = ex.Message }),
                Refused: ex is SecurityAccessDeniedException,
                ex.Message));
        }
    }

    private static IReadOnlyList<string> entriesOrEmpty(List<string> entries) => entries;
}
