namespace Taskboard.ValueObjects;

public sealed record AiChatEventKind : StringValueObject
{
    private static readonly IReadOnlyCollection<string> AllowedValues = new HashSet<string>(StringComparer.Ordinal)
    {
        "message",
        "reasoning",
        "tool_call",
        "activity",
        "error",
        "permission",
        // SPEC-20260921-agent-execution-event-pipeline: kinds normalizados ACP.
        "thought",
        "plan",
        "tool_output",
        "session",
        "output",
        "lifecycle",
        // SPEC-20260921-acp-v1-conformance RF-007: ACP metadata kinds.
        "commands",
        "session_info",
        // SPEC-20260921-ai-code-chat-ux RF-003: usage_update do ACP alimenta o
        // medidor de contexto — precisa persistir com kind próprio.
        "metric"
    };

    public static readonly AiChatEventKind Message = new("message");
    public static readonly AiChatEventKind Reasoning = new("reasoning");
    public static readonly AiChatEventKind ToolCall = new("tool_call");
    public static readonly AiChatEventKind Activity = new("activity");
    public static readonly AiChatEventKind Error = new("error");
    public static readonly AiChatEventKind Permission = new("permission");

    public AiChatEventKind(string value)
        : base(value, AllowedValues)
    {
    }

    public static bool IsValid(string value) => !string.IsNullOrWhiteSpace(value) && AllowedValues.Contains(value);

    public static AiChatEventKind From(string value) => new(value);
}
