using Taskboard;
using Taskboard.ValueObjects;

namespace Taskboard.Domain.Entities;

public sealed class WorkflowWorkspace : Entity<WorkspaceId>
{
    public string Workspace { get; private set; } = default!;
    public DateTime UpdatedAt { get; private set; }

    private WorkflowWorkspace()
    {
    }

    private WorkflowWorkspace(WorkspaceId id, string workspace, DateTime updatedAt)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(workspace))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Workspace cannot be empty.");
        }

        Workspace = workspace;
        UpdatedAt = updatedAt;
    }

    public static WorkflowWorkspace Create(WorkspaceId workspaceId, string workspace, DateTime? now = null)
        => new(workspaceId, workspace, now ?? DateTime.UtcNow);

    public void Update(string workspace, DateTime? now = null)
    {
        if (string.IsNullOrWhiteSpace(workspace))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Workspace cannot be empty.");
        }

        Workspace = workspace;
        UpdatedAt = now ?? DateTime.UtcNow;
    }
}
