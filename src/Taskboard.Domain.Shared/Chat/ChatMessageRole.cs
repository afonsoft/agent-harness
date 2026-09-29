namespace Taskboard.ValueObjects;

/// <summary>Role of a persisted chat message (SPEC-20260929-ai-code-provider-chat RF-004).</summary>
public sealed record ChatMessageRole : StringValueObject
{
    private static readonly IReadOnlyCollection<string> AllowedValues = new HashSet<string>(StringComparer.Ordinal)
    {
        "user",
        "assistant",
        "tool"
    };

    public static readonly ChatMessageRole User = new("user");
    public static readonly ChatMessageRole Assistant = new("assistant");
    public static readonly ChatMessageRole Tool = new("tool");

    public ChatMessageRole(string value)
        : base(value, AllowedValues)
    {
    }

    public static ChatMessageRole From(string value) => new(value);
}
