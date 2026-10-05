namespace Taskboard.ValueObjects;

/// <summary>Identifier of a provider-chat tool approval (SPEC-20261005-chat-tool-approval).</summary>
public sealed record ChatApprovalId : StringIdBase
{
    public ChatApprovalId(string value)
        : base(value)
    {
    }

    public static ChatApprovalId From(string value) => new(value);

    public static ChatApprovalId NewGuid() => new(Guid.NewGuid().ToString("N"));
}
