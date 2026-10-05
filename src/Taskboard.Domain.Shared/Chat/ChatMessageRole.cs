namespace Taskboard.ValueObjects;

/// <summary>Role of a persisted chat message (SPEC-20260929-ai-code-provider-chat RF-004).</summary>
public sealed record ChatMessageRole : StringValueObject
{
    private static readonly IReadOnlyCollection<string> AllowedValues = new HashSet<string>(StringComparer.Ordinal)
    {
        "user",
        "assistant",
        "tool",
        // SPEC-20261005-chat-tool-approval RNF-003: audit note rows (approval
        // decisions, preset changes) — rendered inline, never sent to the model.
        "system"
    };

    public static readonly ChatMessageRole User = new("user");
    public static readonly ChatMessageRole Assistant = new("assistant");
    public static readonly ChatMessageRole Tool = new("tool");
    public static readonly ChatMessageRole System = new("system");

    public ChatMessageRole(string value)
        : base(value, AllowedValues)
    {
    }

    public static ChatMessageRole From(string value) => new(value);
}
