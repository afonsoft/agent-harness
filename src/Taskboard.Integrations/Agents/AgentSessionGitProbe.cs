using Taskboard.Application.Contracts.Agents;
using Taskboard.Integrations.Harness;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// SPEC-20261020-session-history-parity RF-001: repo/branch enrichment via
/// bounded git calls (2s each) through <see cref="GitCommandRunner"/> —
/// arguments go via ArgumentList, never shell-interpolated.
/// </summary>
public sealed class AgentSessionGitProbe : IAgentSessionGitProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    private readonly IGitCommandRunner _git;

    public AgentSessionGitProbe(IGitCommandRunner git)
    {
        _git = git;
    }

    public async Task<(string? RepoName, string? Branch)> ProbeAsync(
        string cwd, CancellationToken ct = default)
    {
        var root = await RunAsync(cwd, ["rev-parse", "--show-toplevel"], ct)
            .ConfigureAwait(false);
        if (root is not { Length: > 0 })
        {
            return (null, null);
        }

        var repo = Path.GetFileName(root.TrimEnd('/', '\\'));
        var branch = await RunAsync(cwd, ["branch", "--show-current"], ct)
            .ConfigureAwait(false);
        return (repo, branch is { Length: > 0 } ? branch : null);
    }

    private async Task<string?> RunAsync(
        string cwd, IReadOnlyList<string> args, CancellationToken ct)
    {
        try
        {
            var result = await _git.RunAsync(cwd, args, ProbeTimeout, ct)
                .ConfigureAwait(false);
            return result is { ExitCode: 0, TimedOut: false }
                ? result.StandardOutput.Trim()
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
