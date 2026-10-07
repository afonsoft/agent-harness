using Microsoft.Extensions.Configuration;
using Taskboard.ValueObjects;

namespace Taskboard.Application.Contracts.Chat;

/// <summary>Resolution of the gate for one tool call (SPEC-20261005-chat-tool-approval).</summary>
public enum ChatApprovalDecision
{
    /// <summary>Execute without asking.</summary>
    Allow,

    /// <summary>Suspend the call and prompt the user.</summary>
    Ask,

    /// <summary>Synthetic deny — the call never executes (fail-closed).</summary>
    Deny,
}

/// <summary>
/// SPEC-20261013-chat-risk-approvals RF-004: the gate's full resolution —
/// the decision plus, for the <c>auto</c> policy, the classifier verdict
/// that produced it (medium → badge; high → reason on the card).
/// </summary>
public sealed record ChatGateResolution(
    ChatApprovalDecision Decision,
    ChatToolRiskVerdict? Verdict = null,
    /// <summary>Audit label — e.g. <c>auto:medium</c>, <c>tool-policy:auto</c>.</summary>
    string? Via = null);

/// <summary>
/// SPEC-20261005-chat-tool-approval RF-006/RF-008/RF-009: resolves
/// <c>preset → policy(tool)</c> for the run-loop gate. Order
/// (§3): global disable → per-tool <c>never|allow|ask</c> override →
/// per-conversation allowed-list → preset.
/// </summary>
public static class ChatApprovalPolicy
{
    public const string EnabledKey = "Taskboard:Chat:Approval:Enabled";
    public const string PresetKey = "Taskboard:Chat:Approval:Preset";
    public const string TimeoutSecondsKey = "Taskboard:Chat:Approval:TimeoutSeconds";
    public const string ToolPolicyPrefix = "Taskboard:Chat:Approval:ToolPolicy:";

    public const int DefaultTimeoutSeconds = 120;

    /// <summary>
    /// Resolves the decision for <paramref name="toolName"/> under the
    /// conversation's preset. <paramref name="mutating"/> is the tool's own
    /// <see cref="IChatTool.RequiresConfirmation"/> flag OR membership in the
    /// builtin mutating set (<see cref="ChatCapabilityRules.MutatingTools"/>).
    /// </summary>
    public static ChatApprovalDecision Resolve(
        IConfiguration configuration,
        string preset,
        IReadOnlySet<string> conversationAllowedTools,
        string toolName,
        bool mutating)
    {
        // RF-009: global kill switch — full behavior for every conversation.
        if (!IsEnabled(configuration))
        {
            return ChatApprovalDecision.Allow;
        }

        // Per-tool policy override wins over everything below (§3).
        var toolPolicy = configuration[$"{ToolPolicyPrefix}{toolName}"];
        if (string.Equals(toolPolicy, "never", StringComparison.OrdinalIgnoreCase))
        {
            return ChatApprovalDecision.Deny;
        }

        if (string.Equals(toolPolicy, "allow", StringComparison.OrdinalIgnoreCase))
        {
            return ChatApprovalDecision.Allow;
        }

        if (string.Equals(toolPolicy, "ask", StringComparison.OrdinalIgnoreCase))
        {
            return mutating ? ChatApprovalDecision.Ask : ChatApprovalDecision.Allow;
        }

        // Per-conversation allowed-list (RF-004 rememberTool).
        if (conversationAllowedTools.Contains(toolName))
        {
            return ChatApprovalDecision.Allow;
        }

        return preset switch
        {
            // chat: mutating calls refused pre-execution (RF-006).
            ChatPermissionPresets.Chat when mutating => ChatApprovalDecision.Deny,
            ChatPermissionPresets.Full => ChatApprovalDecision.Allow,
            // ask (and unknown presets — fail-safe): prompt for mutating calls.
            _ when mutating => ChatApprovalDecision.Ask,
            _ => ChatApprovalDecision.Allow,
        };
    }

    /// <summary>
    /// SPEC-20261013 RF-003/RF-004: same resolution order as
    /// <see cref="Resolve"/>, extended with the <c>auto</c> policy — per-tool
    /// <c>auto</c> and preset <c>auto</c> both consult the classifier
    /// (<paramref name="classify"/>). Tools flagged
    /// <paramref name="requiresConfirmation"/> never auto-approve (RF-005):
    /// under <c>auto</c> they fall back to the normal ask card.
    /// </summary>
    public static ChatGateResolution ResolveDetailed(
        IConfiguration configuration,
        string preset,
        IReadOnlySet<string> conversationAllowedTools,
        string toolName,
        bool mutating,
        bool requiresConfirmation,
        Func<ChatToolRiskVerdict> classify)
    {
        if (!IsEnabled(configuration))
        {
            return new ChatGateResolution(ChatApprovalDecision.Allow);
        }

        var toolPolicy = configuration[$"{ToolPolicyPrefix}{toolName}"];
        if (string.Equals(toolPolicy, "never", StringComparison.OrdinalIgnoreCase))
        {
            return new ChatGateResolution(ChatApprovalDecision.Deny, Via: "tool-policy:never");
        }

        if (string.Equals(toolPolicy, "allow", StringComparison.OrdinalIgnoreCase))
        {
            return new ChatGateResolution(ChatApprovalDecision.Allow, Via: "tool-policy:allow");
        }

        if (string.Equals(toolPolicy, "ask", StringComparison.OrdinalIgnoreCase))
        {
            return new ChatGateResolution(
                mutating ? ChatApprovalDecision.Ask : ChatApprovalDecision.Allow,
                Via: "tool-policy:ask");
        }

        if (string.Equals(toolPolicy, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveAuto(mutating, requiresConfirmation, classify, "tool-policy:auto");
        }

        if (conversationAllowedTools.Contains(toolName))
        {
            return new ChatGateResolution(ChatApprovalDecision.Allow, Via: "allowed-list");
        }

        return preset switch
        {
            ChatPermissionPresets.Chat when mutating =>
                new ChatGateResolution(ChatApprovalDecision.Deny, Via: "preset:chat"),
            ChatPermissionPresets.Full =>
                new ChatGateResolution(ChatApprovalDecision.Allow, Via: "preset:full"),
            ChatPermissionPresets.Auto =>
                ResolveAuto(mutating, requiresConfirmation, classify, "preset:auto"),
            _ when mutating =>
                new ChatGateResolution(ChatApprovalDecision.Ask, Via: "preset:ask"),
            _ => new ChatGateResolution(ChatApprovalDecision.Allow, Via: "preset:ask"),
        };
    }

    /// <summary>low → silent; medium → allow + notice; high → ask. RequiresConfirmation hard-gates.</summary>
    private static ChatGateResolution ResolveAuto(
        bool mutating, bool requiresConfirmation, Func<ChatToolRiskVerdict> classify, string via)
    {
        // RF-005: the tool's own confirmation flag outranks the classifier —
        // a RequiresConfirmation tool always prompts, whatever the risk tier.
        if (requiresConfirmation)
        {
            return new ChatGateResolution(
                ChatApprovalDecision.Ask,
                Via: $"{via} (requires-confirmation)");
        }

        var verdict = classify();
        return verdict.Risk switch
        {
            ChatToolRisk.High => new ChatGateResolution(ChatApprovalDecision.Ask, verdict, via),
            ChatToolRisk.Medium => new ChatGateResolution(ChatApprovalDecision.Allow, verdict, via),
            _ => new ChatGateResolution(ChatApprovalDecision.Allow, Via: via),
        };
    }

    /// <summary>RF-009 — default true; false = <c>full</c> behavior everywhere.</summary>
    public static bool IsEnabled(IConfiguration configuration)
    {
        var raw = configuration[EnabledKey];
        return string.IsNullOrWhiteSpace(raw) || (bool.TryParse(raw, out var on) && on);
    }

    /// <summary>Global default preset for new conversations (RF-006) — invalid/blank = ask.</summary>
    public static string DefaultPreset(IConfiguration configuration)
    {
        var raw = configuration[PresetKey];
        return ChatPermissionPresets.IsValid(raw) ? raw! : ChatPermissionPresets.Ask;
    }

    /// <summary>RF-005 — seconds before a pending approval expires unavailable; 0 = wait forever.</summary>
    public static int TimeoutSeconds(IConfiguration configuration)
    {
        var raw = configuration[TimeoutSecondsKey];
        return int.TryParse(raw, out var seconds) && seconds >= 0 ? seconds : DefaultTimeoutSeconds;
    }
}
