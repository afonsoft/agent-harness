using System.Text.Json;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// Runs an installed agent CLI non-interactively inside the workspace
/// (RF-006) — the binary comes from the server-side AgentCliMap allowlist,
/// never from model input; arguments are passed as an argv list (no shell).
/// </summary>
public sealed class RunCliTool(ISecretRedactor redactor) : IChatTool
{
    public string Name => "run_cli";
    public string Description =>
        "Run an installed agent CLI non-interactively inside the workspace (e.g. `devin exec \"...\"`, "
        + "`taskctl --help`). The binary must be one of the installed Harness CLIs; arguments are passed "
        + "directly to the process (no shell).";
    public string ParametersJson => """
        {"type":"object","properties":{"cli":{"type":"string","description":"Installed CLI binary name (e.g. devin, claude, opencode, taskctl)"},"args":{"type":"array","items":{"type":"string"},"description":"Arguments for the CLI"}},"required":["cli"]}
        """;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var cli = arguments.TryGetProperty("cli", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString() ?? string.Empty
            : string.Empty;
        var args = arguments.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString() ?? string.Empty).ToList()
            : [];

        if (string.IsNullOrWhiteSpace(cli))
        {
            return new ChatToolResult(JsonSerializer.Serialize(new { error = "cli is required" }), Refused: true, "empty cli");
        }

        // Allowlist: only binaries that resolve on PATH and belong to the known
        // CLI set (or taskctl itself). The model cannot name arbitrary binaries.
        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            "devin", "claude", "codex", "opencode", "agy", "antigravity", "cline", "taskctl",
        };
        if (!known.Contains(cli))
        {
            return new ChatToolResult(
                JsonSerializer.Serialize(new { error = $"cli '{cli}' is not an allowlisted Harness CLI" }),
                Refused: true, "cli not allowlisted");
        }

        var resolved = ResolveOnPath(cli);
        if (resolved is null)
        {
            return new ChatToolResult(
                JsonSerializer.Serialize(new { error = $"cli '{cli}' is not installed on the host" }));
        }

        var result = await ChatProcessRunner.RunAsync(resolved, args, context.WorkspacePath,
            TimeSpan.FromSeconds(120), cancellationToken).ConfigureAwait(false);
        var output = redactor.Redact(ChatProcessRunner.Truncate(
            $"exitCode: {result.ExitCode}\nstdout:\n{result.Stdout}\nstderr:\n{result.Stderr}"));
        return new ChatToolResult(JsonSerializer.Serialize(new
        {
            cli,
            exitCode = result.ExitCode,
            timedOut = result.TimedOut,
            output,
        }));
    }

    private static string? ResolveOnPath(string binary)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var found = pathEnv
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Join(dir.Trim(), binary))
            .FirstOrDefault(File.Exists);
        if (found is not null)
        {
            return found;
        }

        return null;
    }
}
