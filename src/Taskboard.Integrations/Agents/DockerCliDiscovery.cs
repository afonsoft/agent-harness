using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Dtos;
using Taskboard.Integrations.Skills;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// SPEC-20260928-ai-code-generic-cli RF-004: discovers running Docker
/// containers and probes which builtin agent CLIs exist inside each via
/// <c>docker exec &lt;c&gt; which &lt;cli&gt;</c>. All probes are bounded
/// (5s), cached briefly, and degrade to "unavailable" — never throws to the
/// request path when the daemon is absent.
/// </summary>
public sealed class DockerCliDiscovery : IContainerCliDiscovery
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AvailabilityCache = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ContainersCache = TimeSpan.FromSeconds(10);

    private readonly string _homeDirectory;
    private readonly ILogger<DockerCliDiscovery> _logger;
    private readonly Func<string, string?> _locator;
    private readonly ISkillsInstallRunner _runner;
    private readonly TimeProvider _time;

    private bool _available;
    private DateTimeOffset _availabilityCheckedAt = DateTimeOffset.MinValue;
    private IReadOnlyList<DockerContainerDto> _containers = [];
    private DateTimeOffset _containersAt = DateTimeOffset.MinValue;

    public DockerCliDiscovery(
        string homeDirectory,
        ILogger<DockerCliDiscovery> logger,
        Func<string, string?>? executableLocator = null,
        ISkillsInstallRunner? runner = null,
        TimeProvider? timeProvider = null)
    {
        _homeDirectory = homeDirectory;
        _logger = logger;
        _locator = executableLocator ?? PathSearch.FindExecutable;
        _runner = runner ?? ProcessSkillsInstallRunner.Instance;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Whether the docker daemon answers <c>docker info</c> — cached for 30s.</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        if (now - _availabilityCheckedAt < AvailabilityCache)
        {
            return _available;
        }

        var docker = _locator("docker");
        if (docker is null)
        {
            _available = false;
            _availabilityCheckedAt = now;
            return false;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);
            var result = await _runner.RunAsync(docker, _homeDirectory, ["info", "--format", "{{.ServerVersion}}"], timeout.Token)
                .ConfigureAwait(false);
            _available = result.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Docker daemon probe failed.");
            _available = false;
        }

        _availabilityCheckedAt = _time.GetUtcNow();
        return _available;
    }

    /// <summary>
    /// Lists running containers with the builtin CLIs detected inside each —
    /// empty list when the daemon is unreachable. Result cached for 10s.
    /// </summary>
    public async Task<IReadOnlyList<DockerContainerDto>> ListContainersAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        if (now - _containersAt < ContainersCache)
        {
            return _containers;
        }

        var docker = _locator("docker");
        if (docker is null || !await IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            _containers = [];
            _containersAt = now;
            return _containers;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);
            var result = await _runner
                .RunAsync(docker, _homeDirectory, ["ps", "--format", "{{.Names}}|{{.Image}}"], timeout.Token)
                .ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                _containers = [];
                _containersAt = now;
                return _containers;
            }

            var names = result.StdOut
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.Contains('|'))
                .ToList();

            // SPEC-20261004 RF-005: `which` every declared name (binary + aliases).
            var binaries = AgentCliMap.AllSpecs()
                .SelectMany(kv => kv.Value.DetectionNames)
                .Append("openhands")
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var tasks = names.Select(async line =>
            {
                var parts = line.Split('|');
                var containerName = parts[0];
                var image = parts.Length > 1 ? parts[1] : string.Empty;
                var clis = await ProbeClisAsync(docker, containerName, binaries, cancellationToken)
                    .ConfigureAwait(false);
                return new DockerContainerDto(containerName, image, clis);
            });

            _containers = (await Task.WhenAll(tasks).ConfigureAwait(false)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Docker container listing failed.");
            _containers = [];
        }

        _containersAt = _time.GetUtcNow();
        return _containers;
    }

    private async Task<IReadOnlyList<string>> ProbeClisAsync(
        string docker, string container, IReadOnlyList<string> binaries, CancellationToken cancellationToken)
    {
        var found = new List<string>();
        foreach (var binary in binaries)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ProbeTimeout);
                var result = await _runner
                    .RunAsync(docker, _homeDirectory, ["exec", container, "which", binary], timeout.Token)
                    .ConfigureAwait(false);
                if (result.ExitCode == 0 && result.StdOut.Trim().Length > 0)
                {
                    found.Add(binary);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "CLI probe '{Binary}' in container '{Container}' failed.", binary, container);
            }
        }

        return found;
    }
}
