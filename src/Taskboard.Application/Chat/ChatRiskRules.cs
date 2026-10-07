using System.Text.RegularExpressions;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Application.Chat;

/// <summary>
/// SPEC-20261013-chat-risk-approvals RF-002/RNF: one ordered rule — a regex
/// pattern matched against a tool argument, the tier it implies, and the
/// human-readable reason surfaced on the card/badge. Rules are plain data so
/// tests can enumerate them without reflection.
/// </summary>
public sealed record ChatRiskRule(string Pattern, ChatToolRisk Risk, string Reason)
{
    public bool Matches(string value) =>
        Regex.IsMatch(value, Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

/// <summary>
/// SPEC-20261013 RF-002: the static rules the
/// <see cref="StaticChatToolRiskClassifier"/> walks. Order matters — first
/// match wins within each table.
/// </summary>
public static class ChatRiskRules
{
    /// <summary>Path-like argument keys inspected for workspace escapes.</summary>
    public static readonly IReadOnlyList<string> PathArguments =
        ["path", "source", "destination", "dir", "directory", "file", "filePath", "cwd", "workdir"];

    /// <summary>Tools whose shell-command argument drives the verdict.</summary>
    public static readonly IReadOnlyList<string> ShellCommandArguments = ["command", "script", "code", "cmd"];

    /// <summary>File tools whose `path` argument is jail-checked.</summary>
    public static readonly IReadOnlySet<string> PathTools = new HashSet<string>(StringComparer.Ordinal)
    {
        "write_file", "edit_file", "read_file", "read_image", "list_dir",
        "search_files", "find_files", "run_tests", "git", "apply_patch", "fs_move", "fs_delete",
    };

    /// <summary>
    /// Destructive / host-level shell patterns — always <see cref="ChatToolRisk.High"/>.
    /// </summary>
    public static readonly IReadOnlyList<ChatRiskRule> ShellHigh =
    [
        new(@"\brm\s+(-[a-z]*[rf][a-z]*\b|--(recursive|force)\b)", ChatToolRisk.High, "destructive command (rm -rf)"),
        new(@"\bmkfs\b", ChatToolRisk.High, "formats a filesystem (mkfs)"),
        new(@"\bdd\b[^|\n]*\bof=/dev/", ChatToolRisk.High, "raw write to a block device (dd of=/dev/*)"),
        new(@":\s*\(\s*\)\s*\{", ChatToolRisk.High, "fork bomb (:(){ … })"),
        new(@"\b(shutdown|reboot|halt|poweroff|init\s+[06])\b", ChatToolRisk.High, "host power operation"),
        new(@"\bkill\s+-9\s+-1\b|\bkillall5\b", ChatToolRisk.High, "kills every process"),
        new(@"\bchmod\s+(-[a-z]+\s+)*777\s+/", ChatToolRisk.High, "world-writable system path (chmod -R 777 /)"),
        new(@"[^|]>\s*/dev/(sd|nvme|hd|vd|xvd)", ChatToolRisk.High, "raw write to a block device"),
        new(@"\b(curl|wget)\b[^|\n]*(\||&&|;)\s*(sudo\s+)?(ba|z|fi)?sh\b", ChatToolRisk.High, "pipes a remote download into a shell"),
        new(@"\bgit\s+push\b[^|\n]*(-f\b|--force\b)", ChatToolRisk.High, "force-pushes to the remote"),
        new(@"\b(apt|apt-get|yum|dnf|pacman|zypper|brew|snap)\s+(install|remove|purge|upgrade|update|dist-upgrade)\b", ChatToolRisk.High, "system package mutation"),
        new(@"\bnpm\s+(i|install|ci|uninstall)\s+(-g\b|--global)", ChatToolRisk.High, "global package install"),
        new(@"\bsudo\b", ChatToolRisk.High, "elevated privileges (sudo)"),
        new(@"\b(docker|podman)\s+(rm|rmi|system\s+prune|volume\s+rm)\b", ChatToolRisk.High, "destructive container operation"),
        new(@"\b(fdisk|parted|wipefs|mkfs\.|cryptsetup)\b", ChatToolRisk.High, "disk-level operation"),
        new(@"\b(cat|less|head|tail|tailf|xxd|base64|source|\.)\s+[^\n]*(\.env|secrets?|credentials|id_rsa|id_ed25519|\.pem\b|\.key\b|\.netrc|\.npmrc|\.aws/|\.ssh/)", ChatToolRisk.High, "reads secrets/env material"),
        new(@"\b(printenv\b|env\s*$|export\s+\w*(KEY|TOKEN|SECRET|PASSWORD|CREDENTIAL))", ChatToolRisk.High, "environment/secret access"),
        new(@"\b(crontab|systemctl\s+(enable|start|stop|restart)|service\s+\w+\s+(start|stop|restart))\b", ChatToolRisk.High, "host service mutation"),
    ];

    /// <summary>
    /// Mutating-but-ordinary shell verbs — <see cref="ChatToolRisk.Medium"/>.
    /// Anything not matched by either table is <see cref="ChatToolRisk.Low"/>.
    /// </summary>
    public static readonly IReadOnlyList<ChatRiskRule> ShellMedium =
    [
        new(@"\bgit\s+(add|commit|push|pull|fetch|checkout|switch|merge|rebase|cherry-pick|reset|restore|stash|tag|init|clone|worktree|apply|am|bisect|clean)\b", ChatToolRisk.Medium, "git mutation"),
        new(@"\b(npm|pnpm|yarn|bun|npx|bunx|dotnet|nuget|pip|pip3|pipx|cargo|go|gem|composer|mvn|gradle)\s+(install|add|remove|update|restore|build|test|publish|pack|run|new|create|exec|tool)\b", ChatToolRisk.Medium, "package/build mutation"),
        new(@"\b(mkdir|rmdir|mv|cp|ln|touch|tee|truncate|install|tar|zip|unzip|7z|rsync|scp|ssh|sftp|ftp|patch|chmod|chown|chgrp|kill|pkill|killall)\b", ChatToolRisk.Medium, "file/process mutation"),
        new(@"\bsed\s+(-[a-z]+\s+)*-i\b|\bperl\s+(-[a-z]+\s+)*-pi?\b|\bawk\b[^|\n]*>", ChatToolRisk.Medium, "in-place file edit"),
        new(@"\b(docker|podman|kubectl|helm|terraform|ansible|vagrant|make|cmake|ninja|meson|gcc|g\+\+|clang|javac|mcs|msbuild)\b", ChatToolRisk.Medium, "build/orchestration command"),
        new(@"\b(ba|z|fi)?sh\b|\b(pwsh|powershell|node|deno|python[0-9.]*|ruby|perl|php|lua)\s", ChatToolRisk.Medium, "script execution"),
        new(@"\b(curl|wget|httpie|xh|aria2c)\b", ChatToolRisk.Medium, "network egress"),
        new(@"[^|<>]>>?\s*[^\s|&]", ChatToolRisk.Medium, "writes a file (shell redirect)"),
        new(@"\b(export|unset)\s+\w+=", ChatToolRisk.Medium, "environment mutation"),
        new(@"\b(jobs?\d*|nohup|disown|screen|tmux)\b", ChatToolRisk.Medium, "spawns background work"),
    ];

    /// <summary>
    /// Secrets-sensitive file names — any path arg hitting these is
    /// <see cref="ChatToolRisk.High"/> (env/secret access, RF-002).
    /// </summary>
    public static readonly IReadOnlyList<ChatRiskRule> SecretPaths =
    [
        new(@"(^|/|\\)\.env(\.|$)", ChatToolRisk.High, "reads env file"),
        new(@"(^|/|\\)\.ssh/|id_rsa|id_ed25519", ChatToolRisk.High, "reads ssh material"),
        new(@"(^|/|\\)\.aws/|(^|/|\\)\.config/gcloud", ChatToolRisk.High, "reads cloud credentials"),
        new(@"\.(pem|key|p12|pfx|jks|keystore)$|\.netrc$|\.npmrc$|credentials(\.|$)|secrets?\.(json|ya?ml|toml|env)", ChatToolRisk.High, "reads secret/key material"),
    ];

    /// <summary>
    /// Mutating verb in an MCP/unknown tool name — opaque args default to
    /// <see cref="ChatToolRisk.Medium"/>, a mutating name upgrades to
    /// <see cref="ChatToolRisk.High"/> (SPEC open question #1).
    /// </summary>
    public static readonly ChatRiskRule McpMutatingName =
        new(@"(^|[_:-])(create|write|update|delete|remove|insert|send|post|put|patch|execute|run|invoke|spawn|kill|drop|truncate|set|mutate|commit|push|deploy|publish)([_:-]|$)",
            ChatToolRisk.High, "MCP tool with a mutating name");

    /// <summary>
    /// Per-tool fallback when no pattern matched — tools absent from the
    /// table are <see cref="ChatToolRisk.Low"/>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, ChatToolRisk> ToolDefaults =
        new Dictionary<string, ChatToolRisk>(StringComparer.Ordinal)
        {
            // Writes inside the workspace jail.
            ["write_file"] = ChatToolRisk.Medium,
            ["edit_file"] = ChatToolRisk.Medium,
            ["apply_patch"] = ChatToolRisk.Medium,
            ["fs_move"] = ChatToolRisk.Medium,
            ["fs_copy"] = ChatToolRisk.Medium,
            ["fs_delete"] = ChatToolRisk.Medium,
            // Delegated run creation.
            ["delegate_task"] = ChatToolRisk.Medium,
            ["delegate_fanout"] = ChatToolRisk.Medium,
            ["delegate_coordinate"] = ChatToolRisk.Medium,
            ["run_agent"] = ChatToolRisk.Medium,
            ["task"] = ChatToolRisk.Medium,
            // Executing compute, even sandboxed locally.
            ["code_interpreter"] = ChatToolRisk.Medium,
            ["run_tests"] = ChatToolRisk.Medium,
            // Host-side state mutations.
            ["schedule_create"] = ChatToolRisk.Medium,
            ["schedule_update"] = ChatToolRisk.Medium,
            ["schedule_delete"] = ChatToolRisk.Medium,
            ["job_kill"] = ChatToolRisk.Medium,
            ["generate_image"] = ChatToolRisk.Medium,
            ["attachment_upload"] = ChatToolRisk.Medium,
            ["attachment_delete"] = ChatToolRisk.Medium,
            ["worktree_checkpoint"] = ChatToolRisk.Medium,
            ["worktree_checkpoints"] = ChatToolRisk.Medium,
            ["memory"] = ChatToolRisk.Low, // action drives the verdict — see classifier.
            ["todo"] = ChatToolRisk.Low, // local task list — mutating but never leaves the workspace.
        };
}
