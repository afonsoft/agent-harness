namespace Taskboard.Agents;

/// <summary>
/// Redige segredos de payloads antes de persistir/transmitir
/// (SPEC-20260921-agent-execution-event-pipeline RF-005).
/// </summary>
public interface ISecretRedactor
{
    /// <summary>Substitui padrões de segredo por <c>***</c>. Null-safe.</summary>
    string? Redact(string? text);
}
