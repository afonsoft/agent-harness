namespace Taskboard.Agents;

/// <summary>
/// SPEC-20261104-agent-lineage-notes RF-004: provenance envelope for
/// agent-bound payloads injected into a run terminal's input. The wrapper
/// marks the content as inter-agent traffic — never a user instruction.
/// Plain human chat text is never wrapped.
/// </summary>
public static class MailboxEnvelopeFormatter
{
    public const string OpenTag = "<harness";
    public const string CloseTag = "</harness>";

    /// <summary>
    /// <c>&lt;harness from="&lt;agent&gt; &lt;taskId&gt;" scope="&lt;scope&gt;"
    /// reach="taskctl agent:send \"&lt;reply&gt;\""&gt;…&lt;/harness&gt;</c> —
    /// <paramref name="payload"/> is redacted via <paramref name="redactor"/>
    /// before wrapping so token-class secrets never reach the terminal.
    /// </summary>
    public static string Wrap(
        string fromAgent, string taskId, string scope, string payload,
        ISecretRedactor? redactor)
    {
        var from = $"{Esc(fromAgent)} {Esc(taskId)}".TrimEnd();
        var body = redactor?.Redact(payload) ?? payload;
        return $"{OpenTag} from=\"{from}\" scope=\"{Esc(scope)}\" reach=\"taskctl agent:send \\\"<reply>\\\"\">\n{body}\n{CloseTag}";
    }

    private static string Esc(string? value) =>
        (value ?? string.Empty).Replace("\"", "'", StringComparison.Ordinal).Trim();
}
