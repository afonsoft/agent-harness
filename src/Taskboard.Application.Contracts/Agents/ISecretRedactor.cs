namespace Taskboard.Agents;

/// <summary>
/// Redacts secrets from payloads before persist/broadcast
/// (SPEC-20260921-agent-execution-event-pipeline RF-005).
/// </summary>
public interface ISecretRedactor
{
    /// <summary>Replaces secret patterns with <c>***</c>. Null-safe.</summary>
    string? Redact(string? text);
}
