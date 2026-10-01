using System.Text.Json;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Harness;
using Taskboard.Integrations.Harness.Security;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>Reads a file inside the workspace — path-jailed (RF-006).</summary>
public sealed class ReadFileTool(ISecretRedactor redactor) : IChatTool
{
    public string Name => "read_file";
    public string Description => "Read a text file inside the workspace (path-jailed).";
    public string ParametersJson => """
        {"type":"object","properties":{"path":{"type":"string","description":"Relative path inside the workspace"}},"required":["path"]}
        """;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var path = arguments.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? string.Empty
            : string.Empty;
        try
        {
            var full = PathJailValidator.Validate(path, context.WorkspacePath);
            var content = await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false);
            return new ChatToolResult(JsonSerializer.Serialize(new
            {
                path,
                content = redactor.Redact(ChatProcessRunner.Truncate(content)) ?? string.Empty,
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
public sealed class WriteFileTool() : IChatTool
{
    public string Name => "write_file";
    public string Description => "Create or overwrite a text file inside the workspace (path-jailed).";
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

            await File.WriteAllTextAsync(full, content, cancellationToken).ConfigureAwait(false);
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
