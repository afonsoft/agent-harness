using Taskboard.Dtos;

namespace Taskboard.Agents;

/// <summary>
/// Descoberta de CLIs de agente dentro de contêineres Docker em execução
/// (SPEC-20260929-docker-cli-context) — usada para validar
/// <c>ContainerContext</c> na criação de threads e para oferecer CLIs que só
/// existem dentro do contêiner.
/// </summary>
public interface IContainerCliDiscovery
{
    /// <summary>
    /// Running containers with the builtin CLI binaries detected inside each —
    /// empty when the daemon is unreachable. Result may be briefly cached.
    /// </summary>
    Task<IReadOnlyList<DockerContainerDto>> ListContainersAsync(CancellationToken cancellationToken = default);
}
