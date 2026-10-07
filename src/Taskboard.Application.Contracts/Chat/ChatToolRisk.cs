using System.Text.Json;

namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// SPEC-20261013-chat-risk-approvals RF-001: per-call risk tier produced by
/// the static classifier. Drives the <c>auto</c> approval policy —
/// <see cref="Low"/> executes silently, <see cref="Medium"/> executes with a
/// <c>risk.notice</c> badge + an <c>auto:medium</c> audit row, and
/// <see cref="High"/> suspends on the normal approval card.
/// </summary>
public enum ChatToolRisk
{
    /// <summary>Read-only / harmless — allow without surfacing anything.</summary>
    Low,

    /// <summary>Mutating but ordinary — allow, badge the tool card, audit.</summary>
    Medium,

    /// <summary>Destructive or jail-breaking — always ask, with the reason.</summary>
    High,
}

/// <summary>The classifier's verdict for one tool call (RF-001).</summary>
public sealed record ChatToolRiskVerdict(ChatToolRisk Risk, string Reason)
{
    public static readonly ChatToolRiskVerdict Safe = new(ChatToolRisk.Low, "read-only call");

    /// <summary>Lowercase wire value (<c>low|medium|high</c>).</summary>
    public string RiskValue => Risk switch
    {
        ChatToolRisk.Low => "low",
        ChatToolRisk.Medium => "medium",
        _ => "high",
    };
}

/// <summary>
/// SPEC-20261013 RF-002: classifies a tool call before the approval gate
/// resolves it. The default implementation is a static rules table — pure,
/// no I/O, no LLM (&lt;1ms per call).
/// </summary>
public interface IChatToolRiskClassifier
{
    ChatToolRiskVerdict Classify(string toolName, JsonElement arguments, ChatToolContext context);
}
