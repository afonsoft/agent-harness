using System.Text.Json;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Harness;
using Taskboard.Integrations.Harness.Security;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// Reads an image for the model — workspace file or <c>attach://{id}</c>
/// (SPEC-20261005-chat-attachments-feedback RF-003/RF-004). The result's
/// <see cref="ChatToolResult.ImageDataUrls"/> feeds the wire <c>image_url</c>
/// part; the JSON keeps only a descriptor so persisted rows stay small.
/// </summary>
public sealed class ReadImageTool(ChatAttachmentStore? attachmentStore = null) : IChatTool
{
    public string Name => "read_image";
    public string Description =>
        "Read an image so you can see it — a workspace-relative path or attach://{id} "
        + "for a composer attachment. Only image/* types are accepted; other attach:// "
        + "types go through read_file.";
    public string ParametersJson => """
        {"type":"object","properties":{"path":{"type":"string","description":"Workspace-relative image path or attach://{id}"}},"required":["path"]}
        """;

    private const string AttachScheme = "attach://";
    private const long MaxImageBytes = 8L * 1024 * 1024;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var path = arguments.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? string.Empty
            : string.Empty;

        string full;
        string mime;
        if (path.StartsWith(AttachScheme, StringComparison.OrdinalIgnoreCase))
        {
            var resolved = attachmentStore?.Resolve(path[AttachScheme.Length..]);
            if (resolved is null)
            {
                return new ChatToolResult(
                    JsonSerializer.Serialize(new { error = $"attachment '{path[AttachScheme.Length..]}' not found" }),
                    RefusalReason: "attachment not found");
            }

            (full, mime) = resolved.Value;
            if (!mime.StartsWith("image/", StringComparison.Ordinal))
            {
                return new ChatToolResult(JsonSerializer.Serialize(new
                {
                    path,
                    contentType = mime,
                    error = "read_image accepts image/* attachments only — use read_file for other types",
                }), Refused: true, "not an image");
            }
        }
        else
        {
            try
            {
                full = PathJailValidator.Validate(path, context.WorkspacePath);
            }
            catch (SecurityAccessDeniedException ex)
            {
                return new ChatToolResult(
                    JsonSerializer.Serialize(new { error = ex.Message }), Refused: true, ex.Message);
            }

            if (!File.Exists(full))
            {
                return new ChatToolResult(
                    JsonSerializer.Serialize(new { error = $"file not found: {path}" }),
                    RefusalReason: "file not found");
            }

            mime = GuessMime(full);
            if (!mime.StartsWith("image/", StringComparison.Ordinal))
            {
                return new ChatToolResult(JsonSerializer.Serialize(new
                {
                    path,
                    error = "read_image accepts image files only",
                }), Refused: true, "not an image");
            }
        }

        var bytes = await File.ReadAllBytesAsync(full, cancellationToken).ConfigureAwait(false);
        if (bytes.LongLength > MaxImageBytes)
        {
            return new ChatToolResult(JsonSerializer.Serialize(new
            {
                path,
                error = $"image exceeds the {MaxImageBytes} byte limit",
            }), Refused: true, "image too large");
        }

        return new ChatToolResult(
            JsonSerializer.Serialize(new { path, contentType = mime, bytes = bytes.LongLength }),
            ImageDataUrls: [$"data:{mime};base64,{Convert.ToBase64String(bytes)}"]);
    }

    private static string GuessMime(string fullPath) => Path.GetExtension(fullPath).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream",
    };
}

/// <summary>
/// Model-declared deliverables (SPEC-20261005-chat-attachments-feedback
/// RF-007): the model lists the workspace paths it produced so they surface
/// on the run's deliverables card even without a git diff.
/// </summary>
public sealed class PresentTool(IChatFileEditTracker? editTracker = null) : IChatTool
{
    public string Name => "present";
    public string Description =>
        "Declare files you produced or changed as deliverables of this run — they show "
        + "on the run summary card. Call once near the end with every relevant path.";
    public string ParametersJson => """
        {"type":"object","properties":{"files":{"type":"array","items":{"type":"object","properties":{"path":{"type":"string","description":"Workspace-relative path"},"summary":{"type":"string","description":"One-line description of the deliverable"}},"required":["path"]},"description":"Files to present (max 20)"}},"required":["files"]}
        """;

    private const int MaxFiles = 20;

    public Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        if (context.RunId is null || context.ConversationId is null)
        {
            return Task.FromResult(new ChatToolResult(
                JsonSerializer.Serialize(new { presented = 0, note = "no active run context" })));
        }

        var declared = new List<string>();
        if (arguments.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
        {
            foreach (var file in files.EnumerateArray().Take(MaxFiles))
            {
                var path = file.TryGetProperty("path", out var fp) && fp.ValueKind == JsonValueKind.String
                    ? fp.GetString() ?? string.Empty
                    : string.Empty;
                if (path.Length == 0)
                {
                    continue;
                }

                // Validate the path stays inside the workspace when it exists
                // there — a declared file outside the jail is rejected.
                try
                {
                    PathJailValidator.Validate(path, context.WorkspacePath);
                }
                catch (SecurityAccessDeniedException)
                {
                    continue;
                }

                var summary = file.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String
                    ? s.GetString()
                    : null;
                editTracker?.Declare(context.RunId, context.ConversationId, path, summary);
                declared.Add(path);
            }
        }

        return Task.FromResult(new ChatToolResult(JsonSerializer.Serialize(new
        {
            presented = declared.Count,
            files = declared,
        })));
    }
}
