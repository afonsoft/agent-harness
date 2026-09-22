namespace Taskboard.ValueObjects;

public sealed record AiChatEventRole : StringValueObject
{
    private static readonly IReadOnlyCollection<string> AllowedValues = new HashSet<string>(StringComparer.Ordinal)
    {
        "user",
        "assistant",
        "activity",
        "error",
        // SPEC-20260921-ai-code-chat-ux RF-002: prompts enfileirados durante
        // turno ativo persistem para sobreviver a reload.
        "queued"
    };

    public static readonly AiChatEventRole User = new("user");
    public static readonly AiChatEventRole Assistant = new("assistant");
    public static readonly AiChatEventRole Activity = new("activity");
    public static readonly AiChatEventRole Error = new("error");
    public static readonly AiChatEventRole Queued = new("queued");

    public AiChatEventRole(string value)
        : base(value, AllowedValues)
    {
    }

    public static AiChatEventRole From(string value) => new(value);
}
