using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Integrations.Skills;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Probes each supported agent CLI for installation (PATH) and authentication
/// (credential-file existence only — contents are never read). SPEC-20260917-cli-agents-terminal RF-003.
/// <para>
/// SPEC-20260928-agent-cli-probe-background: the slow <c>--version</c> subprocess
/// no longer runs inline — versions come from <see cref="CliProbeSnapshotService"/>,
/// refreshed in the background. Reads stay under ~100ms and trigger a refresh
/// when the snapshot is older than the configured TTL.
/// </para>
/// </summary>
public sealed class AgentCliStatusService : IAgentCliStatusService
{
    private readonly string _homeDirectory;
    private readonly ILogger<AgentCliStatusService> _logger;
    private readonly Func<string, string?> _locator;
    private readonly CliProbeSnapshotService? _snapshot;
    private readonly TimeSpan _refreshTtl;

    public AgentCliStatusService(
        string homeDirectory,
        ILogger<AgentCliStatusService> logger,
        Func<string, string?>? executableLocator = null,
        ISkillsInstallRunner? runner = null,
        CliProbeSnapshotService? snapshot = null,
        TimeSpan? refreshTtl = null)
    {
        _homeDirectory = homeDirectory;
        _logger = logger;
        _locator = executableLocator ?? PathSearch.FindExecutable;
        _snapshot = snapshot;
        _refreshTtl = refreshTtl ?? TimeSpan.FromSeconds(120);
    }

    public Task<IReadOnlyList<AgentCliStatus>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        // Stale snapshot → refresh in background; the response below is served
        // from the last-known snapshot immediately (SPEC-20260928 RF-004).
        _snapshot?.EnsureFresh(_refreshTtl);

        var results = new List<AgentCliStatus>();
        foreach (var (kind, spec) in AgentCliMap.All)
        {
            results.Add(ProbeInline(kind, spec));
        }

        return Task.FromResult<IReadOnlyList<AgentCliStatus>>(results);
    }

    private AgentCliStatus ProbeInline(AgentCliKind kind, AgentCliSpec spec)
    {
        // Cheap probes stay inline so installed/auth are never served stale:
        // PATH lookup and credential-file existence are filesystem reads (~ms).
        var binary = _locator(spec.Binary);
        var installed = binary is not null;
        var auth = ProbeAuth(spec);
        var version = installed ? _snapshot?.GetVersion(kind) : null;

        return new AgentCliStatus(
            kind,
            spec.DisplayName,
            spec.Binary,
            installed,
            version,
            installed ? auth : AgentCliAuthStatus.Unknown,
            spec.ConfigDirDisplay,
            spec.LoginCommand,
            spec.InstallHint,
            spec.Install.RequiredTool,
            _locator(spec.Install.RequiredTool) is not null);
    }

    private AgentCliAuthStatus ProbeAuth(AgentCliSpec spec)
    {
        if (spec.CredentialRelativePath is null)
        {
            return AgentCliAuthStatus.Unknown;
        }

        var path = Path.Join(_homeDirectory, spec.CredentialRelativePath);
        return File.Exists(path) ? AgentCliAuthStatus.Authenticated : AgentCliAuthStatus.NotAuthenticated;
    }
}
