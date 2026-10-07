using System.Text.Json;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// Runs an installed agent CLI non-interactively inside the workspace
/// (RF-006) — the binary comes from the server-side AgentCliMap allowlist,
/// never from model input; arguments are passed as an argv list (no shell).
/// </summary>
public sealed class RunCliTool(
    ISecretRedactor redactor,
    Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "run_cli";
    public string Description =>
        "Execute an installed agent CLI non-interactively inside the workspace and return its "
        + "output (e.g. `devin exec \"...\"`, `taskctl --help`). This actually runs the binary — "
        + "use it to perform CLI actions the task needs instead of printing the command for the "
        + "user. The binary must be one of the installed Harness CLIs; arguments are passed "
        + "directly to the process (no shell).";
    public string ParametersJson => """
        {"type":"object","properties":{"cli":{"type":"string","description":"Installed CLI binary name (e.g. devin, claude, opencode, taskctl); omit to use the conversation's selected agent CLI"},"args":{"type":"array","items":{"type":"string"},"description":"Arguments for the CLI"}}}
        """;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var cli = ResolveCliName(arguments, context);
        if (string.IsNullOrWhiteSpace(cli))
        {
            return new ChatToolResult(JsonSerializer.Serialize(new { error = "cli is required" }), Refused: true, "empty cli");
        }

        var args = arguments.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString() ?? string.Empty).ToList()
            : [];

        // Allowlist: only binaries that resolve on PATH and belong to the known
        // CLI set (or taskctl itself). The model cannot name arbitrary binaries.
        if (!KnownBinaries.Contains(cli))
        {
            var customResult = await TryRunCustomDefAsync(cli, args, context, cancellationToken)
                .ConfigureAwait(false);
            return customResult ?? new ChatToolResult(
                JsonSerializer.Serialize(new { error = $"cli '{cli}' is not an allowlisted Harness CLI" }),
                Refused: true, "cli not allowlisted");
        }

        var resolved = ResolveOnPath(cli) ?? string.Empty;
        if (resolved.Length == 0)
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

    private static readonly HashSet<string> KnownBinaries = new(StringComparer.Ordinal)
    {
        "devin", "claude", "codex", "opencode", "agy", "antigravity", "cline", "taskctl",
    };

    private static string ResolveCliName(JsonElement arguments, ChatToolContext context)
    {
        var cli = arguments.TryGetProperty("cli", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString() ?? string.Empty
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(cli) || context.DefaultAgentCli is not { } bound)
        {
            return cli;
        }

        // SPEC-20261003-ai-code-agent-chat: the Agent bar's CLI (an AgentType
        // name like "Devin") maps to its binary for the allowlist; custom-def
        // ids stay as-is and fail cleanly below.
        return Enum.TryParse<AgentType>(bound, ignoreCase: true, out var boundType)
            && AgentCliMap.CliKindFor(boundType) is { } kind
            && AgentCliMap.GetSpec(kind) is { } spec
            ? spec.Binary
            : bound;
    }

    // SPEC-20261004 RF-007: a custom CLI definition (id, display name or
    // executable) is also allowlisted — user-declared, still not arbitrary
    // model input. Null when no matching def exists.
    private async Task<ChatToolResult?> TryRunCustomDefAsync(
        string cli, IReadOnlyList<string> args, ChatToolContext context,
        CancellationToken cancellationToken)
    {
        var def = await CustomCliRunner.FindAsync(scopeFactory, cli, cancellationToken)
            .ConfigureAwait(false);
        if (def is null)
        {
            return null;
        }

        var resolved = CustomCliRunner.ResolveExecutable(def.Executable) ?? string.Empty;
        if (resolved.Length == 0)
        {
            return new ChatToolResult(
                JsonSerializer.Serialize(new { error = $"custom cli '{def.DisplayName}' is not installed on the host" }));
        }

        return await CustomCliRunner.ExecAsync(
            resolved, args, stdin: null, context.WorkspacePath, redactor,
            cancellationToken, def.DisplayName).ConfigureAwait(false);
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
