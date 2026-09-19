namespace Taskboard.Dtos;

public sealed record WorkflowWorkspaceDto(
    string WorkspaceId,
    string Workspace,
    DateTime UpdatedAt);
