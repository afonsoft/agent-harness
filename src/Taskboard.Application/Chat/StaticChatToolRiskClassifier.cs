using System.Text.Json;
using System.Text.RegularExpressions;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Application.Chat;

/// <summary>
/// SPEC-20261013-chat-risk-approvals RF-002: deterministic per-call
/// classifier — pure, no I/O, no LLM. Order: shell-command table → secret
/// path access → workspace escape → MCP fallback → per-tool default → low.
/// </summary>
public sealed class StaticChatToolRiskClassifier : IChatToolRiskClassifier
{
    /// <summary>Singleton — the classifier is stateless.</summary>
    public static readonly StaticChatToolRiskClassifier Instance = new();

    private static readonly TimeSpan DriveLetterMatchTimeout = TimeSpan.FromMilliseconds(250);

    public ChatToolRiskVerdict Classify(string toolName, JsonElement arguments, ChatToolContext context)
    {
        // Shell-driven calls classify on the command line itself.
        if (toolName is "shell_exec" or "run_cli" or "code_interpreter" or "run_command")
        {
            var command = FirstArg(arguments, ChatRiskRules.ShellCommandArguments)
                ?? ArgsArray(arguments);
            return ClassifyCommand(command ?? string.Empty, toolName);
        }

        // Secret material — read or write, beats the generic escape check.
        foreach (var path in PathArgs(arguments))
        {
            foreach (var rule in ChatRiskRules.SecretPaths)
            {
                if (rule.Matches(path))
                {
                    return new ChatToolRiskVerdict(ChatToolRisk.High, rule.Reason);
                }
            }
        }

        // Any path escaping the workspace jail on a file-touching tool.
        if (ChatRiskRules.PathTools.Contains(toolName))
        {
            foreach (var path in PathArgs(arguments))
            {
                if (EscapesWorkspace(path, context.WorkspacePath))
                {
                    return new ChatToolRiskVerdict(
                        ChatToolRisk.High, $"path escapes the workspace ({Clip(path)})");
                }
            }
        }

        // SPEC-20261016: browser_use — eval_js runs arbitrary page JS (high);
        // the rest is page interaction (medium). RequiresConfirmation still
        // applies on top per SPEC-…-browser-tool RF-007.
        if (toolName is "browser_use")
        {
            var action = FirstArg(arguments, ["action"]);
            return action is "eval_js"
                ? new ChatToolRiskVerdict(ChatToolRisk.High, "eval_js runs arbitrary page JS")
                : new ChatToolRiskVerdict(ChatToolRisk.Medium, "browser page interaction");
        }

        // Memory writes vs reads.
        if (toolName is "memory")
        {
            var action = FirstArg(arguments, ["action"]);
            return action is "add" or "update" or "delete"
                ? new ChatToolRiskVerdict(ChatToolRisk.Medium, "memory write")
                : ChatToolRiskVerdict.Safe;
        }

        // MCP / unknown external tools — opaque args (open question #1).
        if (toolName.StartsWith("mcp:", StringComparison.Ordinal))
        {
            return ChatRiskRules.McpMutatingName.Matches(toolName)
                ? new ChatToolRiskVerdict(ChatToolRisk.High, ChatRiskRules.McpMutatingName.Reason)
                : new ChatToolRiskVerdict(ChatToolRisk.Medium, "MCP tool (opaque args)");
        }

        if (ChatRiskRules.ToolDefaults.TryGetValue(toolName, out var tier))
        {
            return new ChatToolRiskVerdict(tier, $"tool '{toolName}'");
        }

        return ChatToolRiskVerdict.Safe;
    }

    private static ChatToolRiskVerdict ClassifyCommand(string command, string toolName)
    {
        foreach (var rule in ChatRiskRules.ShellHigh)
        {
            if (rule.Matches(command))
            {
                return new ChatToolRiskVerdict(ChatToolRisk.High, rule.Reason);
            }
        }

        foreach (var rule in ChatRiskRules.ShellMedium)
        {
            if (rule.Matches(command))
            {
                return new ChatToolRiskVerdict(ChatToolRisk.Medium, rule.Reason);
            }
        }

        // Bare shell verbs we can't see — read-only by pattern.
        return toolName is "code_interpreter"
            ? new ChatToolRiskVerdict(ChatToolRisk.Medium, "arbitrary code execution")
            : ChatToolRiskVerdict.Safe;
    }

    /// <summary>First non-empty string among the candidate argument keys.</summary>
    private static string? FirstArg(JsonElement arguments, IReadOnlyList<string> keys)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (arguments.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String)
            {
                return p.GetString();
            }
        }

        return null;
    }

    /// <summary>run_cli: the command is `cli` + `args[]` joined.</summary>
    private static string? ArgsArray(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var cli = arguments.TryGetProperty("cli", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString() ?? string.Empty
            : string.Empty;
        if (arguments.TryGetProperty("args", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            return cli + " " + string.Join(' ',
                arr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()));
        }

        return string.IsNullOrWhiteSpace(cli) ? null : cli;
    }

    private static IEnumerable<string> PathArgs(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var key in ChatRiskRules.PathArguments)
        {
            if (arguments.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String)
            {
                var value = p.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }
        }
    }

    /// <summary>
    /// A path leaves the jail when it's absolute, home-relative, contains
    /// <c>..</c>, or resolves outside <paramref name="workspace"/>.
    /// <c>spill://</c>/<c>attach://</c> URIs are virtual — never escapes.
    /// </summary>
    internal static bool EscapesWorkspace(string path, string workspace)
    {
        if (path.StartsWith("spill:", StringComparison.Ordinal)
            || path.StartsWith("attach:", StringComparison.Ordinal))
        {
            return false;
        }

        if (path.Contains("..", StringComparison.Ordinal)
            || path.StartsWith('~')
            || Regex.IsMatch(path, @"^[a-zA-Z]:[\\/]", RegexOptions.None, DriveLetterMatchTimeout)
            || Path.IsPathRooted(path))
        {
            if (!Path.IsPathRooted(path))
            {
                return true; // `..`/tilde relative paths are never safe.
            }

            // Rooted paths are fine only inside the resolved workspace.
            try
            {
                var full = Path.GetFullPath(path);
                var root = Path.GetFullPath(string.IsNullOrWhiteSpace(workspace) ? "/" : workspace);
                return !full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal)
                    && !string.Equals(full, root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return true; // unparseable path — fail closed.
            }
        }

        return false;
    }

    private static string Clip(string value) => value.Length <= 80 ? value : value[..77] + "…";
}
