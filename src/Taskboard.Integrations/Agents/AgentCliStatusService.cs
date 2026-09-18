using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Integrations.Skills;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Probes each supported agent CLI for installation (PATH), version
/// (<c>--version</c>, bounded) and authentication (credential-file existence
/// only — contents are never read). SPEC-20260917-cli-agents-terminal RF-003.
/// </summary>
public sealed class AgentCliStatusService : IAgentCliStatusService
{
    private static readonly TimeSpan VersionProbeTimeout = TimeSpan.FromSeconds(5);
    private static readonly Regex VersionPattern = new(@"\d+\.\d+(\.\d+)?", RegexOptions.Compiled);

    private readonly string _homeDirectory;
    private readonly ILogger<AgentCliStatusService> _logger;
    private readonly Func<string, string?> _locator;
    private readonly ISkillsInstallRunner _runner;

    public AgentCliStatusService(
        string homeDirectory,
        ILogger<AgentCliStatusService> logger,
        Func<string, string?>? executableLocator = null,
        ISkillsInstallRunner? runner = null)
    {
        _homeDirectory = homeDirectory;
        _logger = logger;
        _locator = executableLocator ?? PathSearch.FindExecutable;
        _runner = runner ?? ProcessSkillsInstallRunner.Instance;
    }

    public async Task<IReadOnlyList<AgentCliStatus>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<AgentCliStatus>();
        foreach (var (kind, spec) in AgentCliMap.All)
        {
            results.Add(await ProbeAsync(kind, spec, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<AgentCliStatus> ProbeAsync(
        AgentCliKind kind, AgentCliSpec spec, CancellationToken cancellationToken)
    {
        var binary = _locator(spec.Binary);
        var installed = binary is not null;
        var auth = ProbeAuth(spec);

        string? version = null;
        if (installed)
        {
            version = await ProbeVersionAsync(binary!, cancellationToken).ConfigureAwait(false);
        }

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

    private async Task<string?> ProbeVersionAsync(string binary, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(VersionProbeTimeout);
            var result = await _runner
                .RunAsync(binary, _homeDirectory, ["--version"], timeout.Token)
                .ConfigureAwait(false);

            var output = (result.StdOut + "\n" + result.StdErr).Trim();
            if (output.Length == 0)
            {
                return null;
            }

            var match = VersionPattern.Match(output);
            var version = match.Success ? match.Value : output.Split('\n')[0].Trim();
            return version.Length <= 40 ? version : version[..40];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Version probe failed for {Binary}.", binary);
            return null;
        }
    }
}
