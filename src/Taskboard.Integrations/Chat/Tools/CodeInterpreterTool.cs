using System.Text.Json;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Integrations.Harness.Security;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// Executes a code snippet locally with a chosen runtime — auto-confined
/// (RF-008): snippet written to a temp file under the workspace, timeout +
/// killed process tree, output truncated and secret-scrubbed. Network
/// isolation is best-effort (documented limitation; full isolation needs a
/// container sandbox, out of scope).
/// </summary>
public sealed class CodeInterpreterTool(ISecretRedactor redactor) : IChatTool
{
    private static readonly IReadOnlyDictionary<string, (string FileName, string ArgsPrefix)> Runtimes =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["python3"] = ("python3", "{file}"),
            ["python"] = ("python", "{file}"),
            ["node"] = ("node", "{file}"),
            ["dotnet"] = ("dotnet", "script {file}"),
        };

    public string Name => "code_interpreter";
    public string Description =>
        "Execute a short code snippet locally (python3, node or dotnet-script) and return stdout/stderr/exit code. "
        + "Keep snippets small — execution is time-limited.";
    public string ParametersJson => """
        {"type":"object","properties":{"language":{"type":"string","enum":["python3","node","dotnet"],"description":"Runtime to use"},"code":{"type":"string","description":"The code snippet to execute"}},"required":["language","code"]}
        """;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var language = arguments.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String
            ? l.GetString() ?? string.Empty
            : string.Empty;
        var code = arguments.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString() ?? string.Empty
            : string.Empty;
        if (!Runtimes.TryGetValue(language, out var runtime))
        {
            return new ChatToolResult(
                JsonSerializer.Serialize(new { error = $"unsupported language '{language}' — use python3, node or dotnet" }),
                Refused: true, "unsupported runtime");
        }

        var tmpDir = Path.Combine(context.WorkspacePath, ".chat-tmp");
        Directory.CreateDirectory(tmpDir);
        var file = Path.Combine(tmpDir, $"snippet-{Guid.NewGuid():N}{ExtensionFor(language)}");
        try
        {
            await File.WriteAllTextAsync(file, code, cancellationToken).ConfigureAwait(false);
            var args = runtime.ArgsPrefix.Replace("{file}", file).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .ToList();
            var result = await ChatProcessRunner.RunAsync(runtime.FileName, args, tmpDir,
                TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);
            var output = redactor.Redact(ChatProcessRunner.Truncate(
                $"exitCode: {result.ExitCode}\nstdout:\n{result.Stdout}\nstderr:\n{result.Stderr}"));
            return new ChatToolResult(JsonSerializer.Serialize(new
            {
                language,
                exitCode = result.ExitCode,
                timedOut = result.TimedOut,
                output,
            }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ChatToolResult(JsonSerializer.Serialize(new { error = ex.Message }));
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }

    private static string ExtensionFor(string language) => language switch
    {
        "node" => ".mjs",
        "dotnet" => ".csx",
        _ => ".py",
    };
}
