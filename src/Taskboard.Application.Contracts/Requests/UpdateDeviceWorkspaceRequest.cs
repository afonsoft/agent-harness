using System.Text.Json;

namespace Taskboard.Requests;

public sealed record UpdateDeviceWorkspaceRequest(
    string WorkspaceId,
    JsonElement Workspace);
