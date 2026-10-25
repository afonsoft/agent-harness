namespace Taskboard.Diagnostics;

/// <summary>
/// SPEC-20261024-p1-agent-reliability RF-T9: one tool failure captured on a
/// diagnostics card — the tool name plus the refusal/error reason.
/// </summary>
public sealed record RunToolFailureDto(string Name, string Reason);

/// <summary>
/// SPEC-20261024-p1-agent-reliability RF-T9: diagnostics card auto-attached
/// to failed/cancelled <see cref="ChatRunDto"/> and <c>DelegationTaskDto</c>
/// wire shapes — error class, tool failures, last status events and
/// pressure/compaction stats. All fields are best-effort: sources that do
/// not apply to the run kind stay null/empty.
/// </summary>
public sealed record RunDiagnosticsDto(
    /// <summary>Coarse error class — see <see cref="RunDiagnosticsClassifier.ClassifyError"/>.</summary>
    string ErrorClass,
    /// <summary>Up to <see cref="RunDiagnosticsClassifier.MaxToolFailures"/> tool failures (name + reason).</summary>
    IReadOnlyList<RunToolFailureDto> ToolFailures,
    /// <summary>Up to <see cref="RunDiagnosticsClassifier.MaxEvents"/> derived status events, oldest-first.</summary>
    IReadOnlyList<string> LastEvents,
    /// <summary>Effective context-token limit when context stats were recorded (chat runs only).</summary>
    int? ContextTokensLimit,
    /// <summary>Times the context window was compacted during the run.</summary>
    int CompactionCount,
    /// <summary>Token-pressure bucket at capture time, when known.</summary>
    string? PressureState);

/// <summary>
/// Shared classification helpers for diagnostics cards — pure functions so
/// both the chat app service and the delegation EF repository can build a
/// card without taking a dependency on each other.
/// </summary>
public static class RunDiagnosticsClassifier
{
    public const int MaxEvents = 8;
    public const int MaxToolFailures = 5;
    public const int ReasonMax = 200;

    /// <summary>Wire statuses that warrant an attached card (failed or cancelled-by-request).</summary>
    public static bool IsTerminalFailure(string? status) =>
        status is "failed" or "stopped" or "interrupted" or "cancelled";

    /// <summary>
    /// Buckets a terminal error string into a coarse class. Order matters:
    /// policy refusals beat generic "cancel" wording; provider/transport
    /// issues beat the <c>internal</c> fallback.
    /// </summary>
    public static string ClassifyError(string? status, string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return status is "stopped" or "cancelled" ? "cancelled" : "unknown";
        if (error.Contains("credential-path", StringComparison.Ordinal)
            || error.Contains("refused", StringComparison.OrdinalIgnoreCase))
            return "policy-refusal";
        if (error.Contains("cancel", StringComparison.OrdinalIgnoreCase)
            || error.Contains("stopp", StringComparison.OrdinalIgnoreCase))
            return "cancelled";
        if (error.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || error.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return "timeout";
        if (error.Contains("preflight", StringComparison.OrdinalIgnoreCase))
            return "preflight";
        if (error.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || error.Contains("429", StringComparison.Ordinal)
            || error.Contains("provider", StringComparison.OrdinalIgnoreCase)
            || error.Contains("connection", StringComparison.OrdinalIgnoreCase))
            return "provider";
        return "internal";
    }

    public static string Trim(string s) =>
        s.Length <= ReasonMax ? s : string.Concat(s.AsSpan(0, ReasonMax), "…");
}
