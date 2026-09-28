namespace Taskboard.Dtos;

/// <summary>
/// Running Docker container visible to agent threads
/// (SPEC-20260928-ai-code-generic-cli RF-004). <see cref="AvailableClis"/>
/// lists builtin CLI binaries detected inside the container via
/// <c>docker exec which</c>.
/// </summary>
public sealed record DockerContainerDto(
    string Name,
    string Image,
    IReadOnlyList<string> AvailableClis);
