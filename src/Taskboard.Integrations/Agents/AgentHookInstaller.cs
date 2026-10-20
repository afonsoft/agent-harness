using System.Text.Json;
using System.Text.Json.Nodes;
using Taskboard.Agents;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// SPEC-20261020-agent-attention-model RF-004: installs a managed hook that
/// POSTs hook events to /api/agent-hooks, scoped to the worktree so the
/// user's global CLI config is never touched. Managed entries carry the
/// <see cref="Marker"/> sentinel so install/uninstall never disturbs
/// user-defined hooks.
/// </summary>
public interface IAgentHookInstaller
{
    /// <summary>Writes/merges the managed hook block for a CLI — idempotent.</summary>
    Task<bool> InstallAsync(AgentCliKind cli, string workdir, CancellationToken ct = default);

    /// <summary>Removes only the managed hook entries — preserves user hooks.</summary>
    Task<bool> UninstallAsync(AgentCliKind cli, string workdir, CancellationToken ct = default);
}

public sealed class AgentHookInstaller : IAgentHookInstaller
{
    /// <summary>Sentinel inside managed hook commands/blocks.</summary>
    public const string Marker = "harness-agent-hook";

    private static readonly (AgentCliKind Kind, string Event, string ClaudeEvent)[] ClaudeHooks =
    [
        (AgentCliKind.Claude, "session_start", "SessionStart"),
        (AgentCliKind.Claude, "permission", "PermissionRequest"),
        (AgentCliKind.Claude, "notification", "Notification"),
        (AgentCliKind.Claude, "stop", "Stop"),
    ];

    public async Task<bool> InstallAsync(AgentCliKind cli, string workdir, CancellationToken ct = default) =>
        cli switch
        {
            AgentCliKind.Claude => await InstallClaudeAsync(workdir, ct).ConfigureAwait(false),
            AgentCliKind.Codex => await InstallCodexAsync(workdir, ct).ConfigureAwait(false),
            _ => false,
        };

    public async Task<bool> UninstallAsync(AgentCliKind cli, string workdir, CancellationToken ct = default) =>
        cli switch
        {
            AgentCliKind.Claude => await UninstallClaudeAsync(workdir, ct).ConfigureAwait(false),
            AgentCliKind.Codex => await UninstallCodexAsync(workdir, ct).ConfigureAwait(false),
            _ => false,
        };

    // ---------- Claude: .claude/settings.json in the worktree ----------

    private static string ClaudeSettingsPath(string workdir) =>
        Path.Combine(workdir, ".claude", "settings.json");

    private async Task<bool> InstallClaudeAsync(string workdir, CancellationToken ct)
    {
        var path = ClaudeSettingsPath(workdir);
        var root = await ReadJsonAsync(path, ct).ConfigureAwait(false);
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        root["hooks"] = hooks;

        var changed = false;
        foreach (var (_, evt, claudeEvent) in ClaudeHooks)
        {
            var hookCommand =
                "sh -c 'curl -sf -m 3 -X POST \"$HARNESS_AGENT_HOOK_URL\" "
                + "-H \"Authorization: Bearer $HARNESS_AGENT_HOOK_TOKEN\" "
                + "-H \"Content-Type: application/json\" "
                + "-d \"{\\\"sessionKey\\\":\\\"$HARNESS_AGENT_HOOK_SESSION\\\","
                + "\\\"event\\\":\\\"" + evt + "\\\"}\" >/dev/null 2>&1 || true' # " + Marker;

            var entries = hooks[claudeEvent] as JsonArray ?? new JsonArray();
            hooks[claudeEvent] = entries;

            // Idempotent: a managed entry for this event already exists.
            if (entries.Any(e => e?.ToJsonString().Contains(Marker) == true))
            {
                continue;
            }

            entries.Add(new JsonObject
            {
                ["matcher"] = "*",
                ["hooks"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = hookCommand,
                    },
                },
            });
            changed = true;
        }

        if (changed)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct)
                .ConfigureAwait(false);
        }
        return changed;
    }

    private async Task<bool> UninstallClaudeAsync(string workdir, CancellationToken ct)
    {
        var path = ClaudeSettingsPath(workdir);
        if (!File.Exists(path))
        {
            return false;
        }

        var root = await ReadJsonAsync(path, ct).ConfigureAwait(false);
        if (root["hooks"] is not JsonObject hooks)
        {
            return false;
        }

        var changed = false;
        foreach (var property in hooks.ToList())
        {
            if (property.Value is JsonArray entries)
            {
                var kept = new JsonArray();
                foreach (var entry in entries)
                {
                    if (entry?.ToJsonString().Contains(Marker) != true)
                    {
                        kept.Add(entry?.DeepClone());
                    }
                }
                if (kept.Count != entries.Count)
                {
                    changed = true;
                    if (kept.Count == 0)
                    {
                        hooks.Remove(property.Key);
                    }
                    else
                    {
                        hooks[property.Key] = kept;
                    }
                }
            }
        }
        if (hooks.Count == 0)
        {
            root.Remove("hooks");
        }

        if (changed)
        {
            await File.WriteAllTextAsync(path,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct)
                .ConfigureAwait(false);
        }
        return changed;
    }

    // ---------- Codex: config.toml managed block ----------

    private const string BlockBegin = "# >>> harness-agent-hook >>>";
    private const string BlockEnd = "# <<< harness-agent-hook <<<";

    private static string CodexConfigPath(string workdir) =>
        Path.Combine(workdir, ".codex", "config.toml");

    private async Task<bool> InstallCodexAsync(string workdir, CancellationToken ct)
    {
        var path = CodexConfigPath(workdir);
        var text = File.Exists(path)
            ? await File.ReadAllTextAsync(path, ct).ConfigureAwait(false) : "";

        if (text.Contains(BlockBegin, StringComparison.Ordinal))
        {
            return false;
        }

        var block =
            $"{BlockBegin}\n"
            + "notify = [\"sh\", \"-c\", \"curl -sf -m 3 -X POST \\\"$HARNESS_AGENT_HOOK_URL\\\" "
            + "-H \\\"Authorization: Bearer $HARNESS_AGENT_HOOK_TOKEN\\\" "
            + "-H \\\"Content-Type: application/json\\\" "
            + "-d \\\\\\\"{\\\\\\\"sessionKey\\\\\\\":\\\\\\\"$HARNESS_AGENT_HOOK_SESSION\\\\\\\","
            + "\\\\\\\"event\\\\\\\":\\\\\\\"notification\\\\\\\"}\\\\\\\" >/dev/null 2>&1 || true\"]\n"
            + $"{BlockEnd}\n";

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path,
            (text.Length > 0 ? text.TrimEnd() + "\n\n" : "") + block, ct)
            .ConfigureAwait(false);
        return true;
    }

    private async Task<bool> UninstallCodexAsync(string workdir, CancellationToken ct)
    {
        var path = CodexConfigPath(workdir);
        if (!File.Exists(path))
        {
            return false;
        }

        var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var begin = text.IndexOf(BlockBegin, StringComparison.Ordinal);
        var end = text.IndexOf(BlockEnd, StringComparison.Ordinal);
        if (begin < 0 || end < begin)
        {
            return false;
        }

        var after = text[(end + BlockEnd.Length)..].TrimStart('\n', '\r');
        var before = text[..begin].TrimEnd();
        await File.WriteAllTextAsync(path,
            before.Length > 0 ? before + "\n" + (after.Length > 0 ? "\n" + after : "") : after, ct)
            .ConfigureAwait(false);
        return true;
    }

    private static async Task<JsonObject> ReadJsonAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return new JsonObject();
        }
        try
        {
            return JsonNode.Parse(await File.ReadAllTextAsync(path, ct)
                .ConfigureAwait(false)) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject(); // unreadable settings → start fresh managed tree
        }
    }
}
