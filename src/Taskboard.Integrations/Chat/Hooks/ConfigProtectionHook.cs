using System.Text.Json;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Integrations.Harness.Security;
using Taskboard.Workspace;

namespace Taskboard.Integrations.Chat.Hooks;

/// <summary>
/// SPEC-20261024-p1-agent-reliability RF-E03: <c>tool.before</c> hook on
/// <c>write_file|edit_file</c> — blocks calls aimed at credential paths (the
/// <see cref="SensitivePathDenylist"/> verdict classes, same as
/// <see cref="PathJailValidator"/>) or anywhere under the Harness config
/// storage dir. Fail-closed (<see cref="IChatHook.BlockOnFailure"/>).
/// </summary>
public sealed class ConfigProtectionHook(SensitivePathDenylist? denylist, string configStorageDir) : IChatHook
{
    /// <summary>Argument keys scanned for the target path, in priority order.</summary>
    private static readonly string[] PathArgs = ["path", "file_path", "filePath", "target_file"];

    public string Name => "config-protection";
    public string Event => ChatHookEvents.ToolBefore;
    public string Matcher => "write_file|edit_file";
    public bool BlockOnFailure => true;

    public Task<HookVerdict> RunAsync(HookContext context, CancellationToken cancellationToken)
    {
        if (ExtractPath(context.Arguments) is not { } arg)
        {
            return Task.FromResult(HookVerdict.Allow);
        }

        var resolved = Resolve(arg, context.WorkspacePath);
        var list = denylist ?? SensitivePathDenylist.Shared;
        if (list.TryMatch(resolved, out var cls))
        {
            // O verdict delega a classe da denylist — sem vazar o caminho.
            return Task.FromResult(HookVerdict.Block($"credential-path: {cls}", Name));
        }

        if (!string.IsNullOrWhiteSpace(configStorageDir)
            && Path.IsPathFullyQualified(resolved)
            && WorkspacePaths.IsUnder(configStorageDir, resolved))
        {
            return Task.FromResult(HookVerdict.Block("protected config storage", Name));
        }

        return Task.FromResult(HookVerdict.Allow);
    }

    private static string? ExtractPath(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var key in PathArgs)
        {
            if (arguments.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static string Resolve(string path, string? workspacePath)
    {
        try
        {
            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(path, string.IsNullOrWhiteSpace(workspacePath) ? "/" : workspacePath);
        }
        catch (Exception)
        {
            return path;
        }
    }
}
