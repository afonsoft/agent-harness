namespace Taskboard.ValueObjects;

/// <summary>
/// Free-form identifier of a device workflow workspace.
/// Inherited from the former <c>ProjectId</c> — today it is just a workspace
/// key with no link to any entity.
/// </summary>
public sealed record WorkspaceId : StringIdBase
{
    private WorkspaceId(string value)
        : base(value)
    {
    }

    public static WorkspaceId From(string value) => new(value);
}
