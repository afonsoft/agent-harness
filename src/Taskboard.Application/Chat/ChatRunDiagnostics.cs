using System.Text.Json;
using Taskboard.Diagnostics;
using Taskboard.Domain.Entities.Chat;

namespace Taskboard.Application.Chat;

/// <summary>
/// SPEC-20261024-p1-agent-reliability RF-T9: builds the diagnostics card for a
/// terminal <see cref="ChatRun"/> from data already persisted — the run row
/// (lifecycle stamps, error, compaction stats) plus the tool-result messages
/// written since the trigger. Never mutates the run; returns null for
/// non-terminal/successful runs so the wire field stays absent.
/// </summary>
internal static class ChatRunDiagnostics
{
    public static RunDiagnosticsDto? ForRun(ChatRun run, IReadOnlyList<ChatMessage> toolMessages)
    {
        var status = run.Status.Value;
        if (!RunDiagnosticsClassifier.IsTerminalFailure(status))
            return null;

        var events = new List<string> { $"created {run.CreatedAt:O}" };
        if (run.StartedAt is { } started) events.Add($"started {started:O}");
        if (run.PausedAt is { } paused) events.Add($"paused {paused:O}");
        if (run.CompactionCount > 0)
            events.Add($"compaction x{run.CompactionCount} (limit {run.ContextTokensLimit?.ToString() ?? "?"})");
        if (run.FinishedAt is { } finished) events.Add($"finished {finished:O} status={status}");
        if (run.Error is { } error) events.Add($"error: {RunDiagnosticsClassifier.Trim(error)}");

        var failures = toolMessages
            .Where(m => m.ToolName is not null)
            .Select(m => (Name: m.ToolName!, Reason: FailureReason(m)))
            .Where(t => t.Reason is not null)
            .TakeLast(RunDiagnosticsClassifier.MaxToolFailures)
            .Select(t => new RunToolFailureDto(t.Name, t.Reason!))
            .ToList();

        return new RunDiagnosticsDto(
            RunDiagnosticsClassifier.ClassifyError(status, run.Error),
            failures,
            events.TakeLast(RunDiagnosticsClassifier.MaxEvents).ToList(),
            run.ContextTokensLimit,
            run.CompactionCount,
            PressureState: null);
    }

    /// <summary>
    /// A persisted tool message is a failure when its <c>Refused</c> flag is
    /// set (gate/hook refusals persist that flag) or when its result JSON
    /// carries an <c>error</c> string, a true <c>refused</c> flag, or
    /// <c>ok:false</c>. Non-JSON payloads only count when they visibly carry
    /// an error marker.
    /// </summary>
    private static string? FailureReason(ChatMessage message)
    {
        var content = message.Content;
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return message.Refused ? RunDiagnosticsClassifier.Trim(content) : null;
            if (root.TryGetProperty("refused", out var refused) && refused.ValueKind == JsonValueKind.True)
                return Reason(root, "reason", "refusal", content);
            if (root.TryGetProperty("error", out var err)
                && err.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(err.GetString()))
                return RunDiagnosticsClassifier.Trim(err.GetString()!);
            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
                return Reason(root, "message", "detail", content);
            return message.Refused ? Reason(root, "reason", "refusal", content) : null;
        }
        catch (JsonException)
        {
            return message.Refused || content.Contains("\"error\"", StringComparison.Ordinal)
                ? RunDiagnosticsClassifier.Trim(content) : null;
        }
    }

    private static string Reason(JsonElement root, string key, string fallbackKey, string content) =>
        root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { } s && s.Length > 0
            ? RunDiagnosticsClassifier.Trim(s)
            : root.TryGetProperty(fallbackKey, out var f) && f.ValueKind == JsonValueKind.String && f.GetString() is { } fs && fs.Length > 0
                ? RunDiagnosticsClassifier.Trim(fs)
                : RunDiagnosticsClassifier.Trim(content);
}
