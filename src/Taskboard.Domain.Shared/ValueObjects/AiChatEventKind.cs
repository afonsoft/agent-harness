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
        "permission"
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
