using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Integrations.Skills;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Probes installed CLIs for their own model list (<c>opencode models</c>,
/// <c>devin models list</c>, <c>agy models</c>) with a bounded timeout and a
/// short in-memory cache. Feeds the editable model dropdown in the CLI
/// Agents screen; failures degrade to an empty list so the curated
/// <see cref="AgentCliModels"/> catalog still shows.
/// </summary>
public sealed class AgentModelCatalogService : IAgentModelCatalogService
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<AgentType, (DateTimeOffset ExpiresAt, IReadOnlyList<string> Models)> _cache = new();
    private readonly string _homeDirectory;
    private readonly ILogger<AgentModelCatalogService> _logger;
    private readonly Func<string, string?> _locator;
    private readonly ISkillsInstallRunner _runner;

    public AgentModelCatalogService(
        string homeDirectory,
        ILogger<AgentModelCatalogService> logger,
        Func<string, string?>? executableLocator = null,
        ISkillsInstallRunner? runner = null)
    {
        _homeDirectory = homeDirectory;
        _logger = logger;
        _locator = executableLocator ?? PathSearch.FindExecutable;
        _runner = runner ?? ProcessSkillsInstallRunner.Instance;
    }

    public async Task<IReadOnlyList<string>> ListAvailableAsync(
        AgentType agentType, CancellationToken cancellationToken = default)
    {
        var probe = AgentCliModels.ModelListProbe(agentType);
        var kind = AgentCliMap.CliKindFor(agentType);
        var binary = kind is null ? null : AgentCliMap.GetSpec(kind.Value)?.Binary;
        var path = binary is null ? null : _locator(binary);
        if (probe is null || path is null)
        {
            return [];
        }

        if (_cache.TryGetValue(agentType, out var hit) && hit.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return hit.Models;
        }

        IReadOnlyList<string> models;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);
            var result = await _runner
                .RunAsync(path, _homeDirectory, probe.Arguments, timeout.Token)
                .ConfigureAwait(false);

            var output = string.IsNullOrWhiteSpace(result.StdOut) ? result.StdErr : result.StdOut;
            models = AgentModelListParser.Parse(probe.Format, output);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Model-list probe failed for {AgentType} ({Binary}).", agentType, binary);
            models = [];
        }

        _cache[agentType] = (DateTimeOffset.UtcNow.Add(CacheTtl), models);
        return models;
    }
}
