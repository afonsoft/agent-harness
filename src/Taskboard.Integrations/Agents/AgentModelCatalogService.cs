using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Integrations.Skills;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Probes installed CLIs for their own model list (<c>opencode models</c>,
/// <c>devin models list</c>, <c>agy models</c>). Feeds the editable model dropdown
/// in the CLI Agents screen; failures degrade to an empty list so the curated
/// <see cref="AgentCliModels"/> catalog still shows.
/// <para>
/// SPEC-20260928-agent-cli-probe-background: non-forced reads return the
/// <see cref="CliProbeSnapshotService"/> snapshot instantly and schedule a
/// background warm-up on miss — the 10s probe never blocks the dialog.
/// <paramref name="forceRefresh"/> keeps the bounded synchronous probe.
/// </para>
/// </summary>
public sealed class AgentModelCatalogService : IAgentModelCatalogService
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    private readonly string _homeDirectory;
    private readonly ILogger<AgentModelCatalogService> _logger;
    private readonly Func<string, string?> _locator;
    private readonly ISkillsInstallRunner _runner;
    private readonly CliProbeSnapshotService? _snapshot;

    private readonly TimeSpan _refreshTtl;

    public AgentModelCatalogService(
        string homeDirectory,
        ILogger<AgentModelCatalogService> logger,
        Func<string, string?>? executableLocator = null,
        ISkillsInstallRunner? runner = null,
        CliProbeSnapshotService? snapshot = null,
        TimeSpan? refreshTtl = null)
    {
        _homeDirectory = homeDirectory;
        _logger = logger;
        _locator = executableLocator ?? PathSearch.FindExecutable;
        _runner = runner ?? ProcessSkillsInstallRunner.Instance;
        _snapshot = snapshot;
        _refreshTtl = refreshTtl ?? TimeSpan.FromSeconds(120);
    }

    public async Task<IReadOnlyList<string>> ListAvailableAsync(
        AgentType agentType, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var probe = AgentCliModels.ModelListProbe(agentType);
        var kind = AgentCliMap.CliKindFor(agentType);
        var binary = kind is null ? null : AgentCliMap.GetSpec(kind.Value)?.Binary;
        var path = binary is null ? null : _locator(binary);
        if (probe is null || path is null)
        {
            return [];
        }

        // Fast path: serve the last-known snapshot and schedule a background
        // warm-up when the CLI was never probed (first call after restart).
        // Stale snapshots trigger a background refresh too — without this a
        // catalog read path that never touches CLI statuses could serve a
        // model list forever (SPEC-20260929-cli-probe-hardening RF-001).
        if (!forceRefresh)
        {
            _snapshot?.EnsureModelsFresh(agentType, _refreshTtl);
            if (_snapshot?.GetModels(agentType) is { } cached)
            {
                return cached;
            }

            _snapshot?.EnsureRefreshing();
            return [];
        }

        var models = await ProbeAsync(agentType, path, probe, cancellationToken).ConfigureAwait(false);
        _snapshot?.SetModels(agentType, models);
        return models;
    }

    private async Task<IReadOnlyList<string>> ProbeAsync(
        AgentType agentType, string binaryPath, AgentModelListProbe probe, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);
            var result = await _runner
                .RunAsync(binaryPath, _homeDirectory, probe.Arguments, timeout.Token)
                .ConfigureAwait(false);

            var output = string.IsNullOrWhiteSpace(result.StdOut) ? result.StdErr : result.StdOut;
            return AgentModelListParser.Parse(probe.Format, output);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Model-list probe failed for {AgentType} ({Binary}).", agentType, binaryPath);
            return [];
        }
    }
}
