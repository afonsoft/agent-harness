namespace Taskboard.Agents;

/// <summary>
/// SPEC-20261022-cli-autonomy-profiles RF-001: per-CLI argv expressing each
/// <see cref="AgentAutonomy"/> level. The table owns the leading segment
/// (subcommand + permission flags) that <see cref="Taskboard.Application.Contracts.Agents.AgentCliInvocation"/>
/// used to hardcode — <see cref="AgentAutonomy.Edit"/> reproduces the historical
/// argv byte-for-byte (zero regression), <see cref="AgentAutonomy.Plan"/>
/// downgrades to read-only/auto-deny where the CLI supports it, and
/// <see cref="AgentAutonomy.Full"/> applies the strongest verified flag.
///
/// Flags verified against each CLI's current docs (2026-10-22):
/// - claude   `--permission-mode default|acceptEdits|plan|auto|dontAsk|bypassPermissions`,
///            `--dangerously-skip-permissions` (code.claude.com/docs/en/permission-modes —
///            `dontAsk` = reads+pre-approved tools, would-prompt ops auto-denied: the
///            headless-safe plan tier).
/// - codex    `exec --sandbox read-only|workspace-write|danger-full-access`,
///            `--approve-for-me` (auto-review + workspace-write; conflicts with --sandbox),
///            `--dangerously-bypass-approvals-and-sandbox|--yolo` (learn.chatgpt.com/docs/non-interactive-mode).
/// - devin    `--permission-mode auto|dangerous` (auto asks → headless auto-denies).
/// - opencode `run --auto` approves permissions; without it headless auto-denies.
/// - antigravity `--dangerously-skip-permissions`; omitting it auto-denies.
/// - aider    `--yes-always` confirms all prompts (no read-only flag — Plan is advisory).
/// - copilot  `--allow-all-tools`; without it `-p` prompts are auto-denied headless.
/// - kiro     `chat --no-interactive --trust-all-tools`; omitting trust auto-denies.
/// - cn       `run/--auto` approves tool calls; `-p` alone auto-denies.
/// - kimi/grok/qwen/cline: no verified permission flags — all levels advisory
///   (argv unchanged; the worker brief carries the read-only contract).
/// </summary>
public static class AgentCliAutonomy
{
    /// <summary>Leading argv segment for the level — includes the subcommand
    /// (<c>exec</c>/<c>run</c>/<c>chat</c>) where the CLI requires one.</summary>
    public static IReadOnlyList<string> Flags(AgentType cli, AgentAutonomy level) => level switch
    {
        AgentAutonomy.Plan => PlanFlags(cli),
        AgentAutonomy.Full => FullFlags(cli),
        _ => EditFlags(cli),
    };

    /// <summary>
    /// True when the level has no dedicated flag expression for this CLI —
    /// the argv equals a weaker level's and the restriction is prompt-contract
    /// only. Lets callers record honest diagnostics instead of implying
    /// enforcement that does not exist.
    /// </summary>
    public static bool IsAdvisory(AgentType cli, AgentAutonomy level) => cli switch
    {
        // no verified permission flags at all
        AgentType.Kimi or AgentType.Grok or AgentType.Qwen or AgentType.Cline
            or AgentType.OpenHands => true,
        // aider has no read-only flag — Plan is prompt-contract only
        AgentType.Aider when level == AgentAutonomy.Plan => true,
        // these CLIs have no flag stronger than Edit
        AgentType.OpenCode or AgentType.Devin or AgentType.Antigravity
            or AgentType.Aider or AgentType.Copilot or AgentType.Kiro
            or AgentType.Continue when level == AgentAutonomy.Full => true,
        _ => false,
    };

    /// <summary>
    /// Isolation/config gate (RF-003). Returns the DomainException error code
    /// when the level is refused, else <c>null</c>.
    /// </summary>
    public static string? GateError(AgentAutonomy level, bool isIsolated, bool allowFull)
    {
        if (level != AgentAutonomy.Full)
        {
            return null;
        }

        if (!allowFull)
        {
            return "autonomy_full_disabled";
        }

        return isIsolated ? null : "autonomy_requires_isolation";
    }

    /// <summary>
    /// RF-006: autonomy flags accepted in <c>ExtraAgentArgs</c> per CLI, mapped
    /// to the level they imply — a flag is allowed only when its implied level
    /// is at-or-below the task's resolved autonomy. Returns <c>null</c> when the
    /// flag is not an autonomy flag for this CLI.
    /// </summary>
    public static AgentAutonomy? FlagLevel(AgentType cli, string flag, string? value) => cli switch
    {
        AgentType.Claude => flag switch
        {
            "--dangerously-skip-permissions" => AgentAutonomy.Full,
            "--permission-mode" => value?.ToLowerInvariant() switch
            {
                "plan" or "default" or "dontask" => AgentAutonomy.Plan,
                "acceptedits" or "auto" => AgentAutonomy.Edit,
                "bypasspermissions" => AgentAutonomy.Full,
                _ => null, // unknown value — not ours to classify
            },
            _ => null,
        },
        AgentType.Codex => flag switch
        {
            "--approve-for-me" or "--ask-for-approval" or "-a" => AgentAutonomy.Edit,
            "--dangerously-bypass-approvals-and-sandbox" or "--yolo" or "--full-auto" => AgentAutonomy.Full,
            "--sandbox" or "-s" => value?.ToLowerInvariant() switch
            {
                "read-only" => AgentAutonomy.Plan,
                "workspace-write" => AgentAutonomy.Edit,
                "danger-full-access" => AgentAutonomy.Full,
                _ => null,
            },
            _ => null,
        },
        AgentType.Devin => flag switch
        {
            "--permission-mode" => value?.ToLowerInvariant() switch
            {
                "auto" => AgentAutonomy.Plan,
                "dangerous" => AgentAutonomy.Full,
                _ => null,
            },
            "--respect-workspace-trust" => AgentAutonomy.Edit,
            _ => null,
        },
        AgentType.OpenCode => flag == "--auto" ? AgentAutonomy.Edit : null,
        AgentType.Antigravity => flag == "--dangerously-skip-permissions" ? AgentAutonomy.Full : null,
        AgentType.Aider => flag switch
        {
            "--yes-always" => AgentAutonomy.Edit,
            "--no-auto-commits" => AgentAutonomy.Plan, // restriction — allowed at any level
            _ => null,
        },
        AgentType.Copilot => flag == "--allow-all-tools" ? AgentAutonomy.Edit : null,
        AgentType.Kiro => flag == "--trust-all-tools" ? AgentAutonomy.Edit : null,
        AgentType.Continue => flag == "--auto" ? AgentAutonomy.Edit : null,
        _ => null,
    };

    private static IReadOnlyList<string> PlanFlags(AgentType cli) => cli switch
    {
        AgentType.Devin => ["--respect-workspace-trust", "false", "--permission-mode", "auto"],
        AgentType.Claude => ["--permission-mode", "dontAsk"],
        AgentType.Codex => ["exec", "--sandbox", "read-only", "--skip-git-repo-check"],
        // no --auto → headless permission prompts auto-deny (read-only-ish)
        AgentType.OpenCode => ["run"],
        AgentType.Antigravity => [],
        AgentType.Aider => ["--yes-always"], // advisory — no read-only flag exists
        AgentType.Copilot => [],
        AgentType.Continue => [],
        AgentType.Kiro => ["chat", "--no-interactive"],
        _ => [],
    };

    // Edit = the historical always-on argv — byte-for-byte identical to the
    // previous hardcoded flags so existing dispatch behavior is unchanged.
    private static IReadOnlyList<string> EditFlags(AgentType cli) => cli switch
    {
        AgentType.Devin => ["--respect-workspace-trust", "false", "--permission-mode", "dangerous"],
        AgentType.Claude => ["--dangerously-skip-permissions"],
        AgentType.Codex => ["exec", "--approve-for-me", "--skip-git-repo-check"],
        AgentType.OpenCode => ["run", "--auto"],
        AgentType.Antigravity => ["--dangerously-skip-permissions"],
        AgentType.Aider => ["--yes-always"],
        AgentType.Copilot => ["--allow-all-tools"],
        AgentType.Continue => ["--auto"],
        AgentType.Kiro => ["chat", "--no-interactive", "--trust-all-tools"],
        _ => [],
    };

    private static IReadOnlyList<string> FullFlags(AgentType cli) => cli switch
    {
        AgentType.Codex => ["exec", "--dangerously-bypass-approvals-and-sandbox", "--skip-git-repo-check"],
        // claude/devin/antigravity/aider/copilot/kiro/cn: the Edit flag is
        // already the strongest verified expression — Full reuses it (not
        // advisory: the flag itself enforces max permission).
        _ => EditFlags(cli),
    };
}
